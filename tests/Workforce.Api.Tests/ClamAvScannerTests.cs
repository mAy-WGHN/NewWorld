using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Workforce.Infrastructure.Certificates;

namespace Workforce.Api.Tests;

/// <summary>
/// The ClamAV adapter against a fake clamd (TCP): protocol (INSTREAM chunks, zero-length end), verdicts, and fail closed on every failure.
/// </summary>
public sealed class ClamAvScannerTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public ClamAvScannerTests() => _listener.Start();

    public void Dispose() => _listener.Stop();

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private ClamAvScanner Scanner(int timeoutSeconds = 5) =>
        new(new ClamAvOptions { Host = "127.0.0.1", Port = Port, TimeoutSeconds = timeoutSeconds }, NullLogger<ClamAvScanner>.Instance);

    /// <summary>Serves one connection: reads the command and, for INSTREAM, all chunks; answers with <paramref name="reply"/>.</summary>
    private Task<(string Command, byte[] Received)> ServeOnce(string? reply)
    {
        return Task.Run(async () =>
        {
            using var client = await _listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var command = new List<byte>();
            var one = new byte[1];
            while (await stream.ReadAsync(one) == 1)
            {
                if (one[0] == 0)
                {
                    break;
                }

                command.Add(one[0]);
            }

            var received = new List<byte>();
            if (System.Text.Encoding.ASCII.GetString([.. command]) == "zINSTREAM")
            {
                var header = new byte[4];
                while (true)
                {
                    await stream.ReadExactlyAsync(header);
                    var size = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
                    if (size == 0)
                    {
                        break;
                    }

                    var chunk = new byte[size];
                    await stream.ReadExactlyAsync(chunk);
                    received.AddRange(chunk);
                }
            }

            if (reply is not null)
            {
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(reply + "\0"));
            }

            return (System.Text.Encoding.ASCII.GetString([.. command]), received.ToArray());
        });
    }

    [Fact]
    public async Task A_clean_verdict_accepts_and_the_whole_content_arrives_in_chunks_in_order()
    {
        var content = new byte[150_000]; // more than two chunks
        new Random(1).NextBytes(content);
        var server = ServeOnce("stream: OK");

        var clean = await Scanner().IsCleanAsync(content, CancellationToken.None);

        clean.ShouldBeTrue();
        var (command, received) = await server;
        command.ShouldBe("zINSTREAM");
        received.ShouldBe(content);
    }

    [Fact]
    public async Task A_found_verdict_rejects()
    {
        var server = ServeOnce("stream: Eicar-Signature FOUND");

        (await Scanner().IsCleanAsync(new byte[] { 1, 2, 3 }, CancellationToken.None)).ShouldBeFalse();
        await server;
    }

    [Fact]
    public async Task An_error_answer_such_as_the_size_limit_rejects()
    {
        var server = ServeOnce("INSTREAM size limit exceeded. ERROR");

        (await Scanner().IsCleanAsync(new byte[] { 1 }, CancellationToken.None)).ShouldBeFalse();
        await server;
    }

    [Fact]
    public async Task An_unknown_answer_rejects()
    {
        var server = ServeOnce("something unexpected");

        (await Scanner().IsCleanAsync(new byte[] { 1 }, CancellationToken.None)).ShouldBeFalse();
        await server;
    }

    [Fact]
    public async Task A_scanner_that_is_not_reachable_rejects()
    {
        var port = Port;
        _listener.Stop();
        var scanner = new ClamAvScanner(new ClamAvOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 2 }, NullLogger<ClamAvScanner>.Instance);

        (await scanner.IsCleanAsync(new byte[] { 1 }, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_scanner_that_never_answers_rejects_after_the_timeout()
    {
        using var release = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            using var client = await _listener.AcceptTcpClientAsync();
            try
            {
                await Task.Delay(Timeout.Infinite, release.Token); // holds the connection open without ever answering
            }
            catch (OperationCanceledException)
            {
            }
        });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        (await Scanner(timeoutSeconds: 1).IsCleanAsync(new byte[] { 1 }, CancellationToken.None)).ShouldBeFalse();

        watch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(800)); // it waited for the timeout, not for a closed socket
        await release.CancelAsync();
        await server;
    }

    [Fact]
    public async Task A_connection_cut_without_an_answer_rejects()
    {
        var server = Task.Run(async () =>
        {
            using var client = await _listener.AcceptTcpClientAsync();
            // closes immediately
        });

        (await Scanner().IsCleanAsync(new byte[1_000_000], CancellationToken.None)).ShouldBeFalse();
        await server;
    }

    [Fact]
    public async Task Ping_answers_true_on_pong_and_false_otherwise()
    {
        var server = ServeOnce("PONG");
        (await Scanner().PingAsync(CancellationToken.None)).ShouldBeTrue();
        (await server).Command.ShouldBe("zPING");

        var other = ServeOnce("nope");
        (await Scanner().PingAsync(CancellationToken.None)).ShouldBeFalse();
        await other;
    }

    [Fact]
    public async Task The_health_check_is_degraded_not_unhealthy_when_the_scanner_is_down()
    {
        var port = Port;
        _listener.Stop();
        var scanner = new ClamAvScanner(new ClamAvOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 2 }, NullLogger<ClamAvScanner>.Instance);

        var result = await new ClamAvHealthCheck(scanner).CheckHealthAsync(new(), CancellationToken.None);

        result.Status.ShouldBe(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
    }
}

/// <summary>Start-up rules for the scanner setting: mandatory in production, optional only in Development/Testing.</summary>
public sealed class ClamAvConfigurationTests
{
    private static ServiceProvider Build(string environment, Dictionary<string, string?> settings)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = environment, ContentRootPath = Path.Combine(Path.GetTempPath(), "newworld-app") };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        Workforce.Infrastructure.InfrastructureServiceCollectionExtensions.AddWorkforceInfrastructure(services, configuration, env);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Production(string? scannerHost) => new()
    {
        ["ConnectionStrings:workforce"] = "Host=localhost;Database=x",
        ["Workforce:Certificates:EncryptionKey"] = CertificateFileProtector.NewKey(),
        ["Workforce:Certificates:StoragePath"] = Path.Combine(Path.GetTempPath(), "newworld-files"),
        ["Workforce:Certificates:Scanner:Host"] = scannerHost,
    };

    [Fact]
    public void Production_refuses_to_start_without_a_scanner_and_the_message_names_only_the_setting()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Build("Production", Production(null)));

        ex.Message.ShouldContain("Workforce:Certificates:Scanner:Host");
    }

    [Fact]
    public void Production_with_a_scanner_host_uses_clamav()
    {
        using var provider = Build("Production", Production("clamav"));

        provider.GetRequiredService<Workforce.Core.Certificates.IMalwareScanner>().ShouldBeOfType<ClamAvScanner>();
    }

    [Fact]
    public void Development_without_a_scanner_host_uses_the_placeholder()
    {
        using var provider = Build("Development", new() { ["ConnectionStrings:workforce"] = "Host=localhost;Database=x" });

        provider.GetRequiredService<Workforce.Core.Certificates.IMalwareScanner>().ShouldBeOfType<PassThroughMalwareScanner>();
    }

    [Fact]
    public void An_invalid_port_stops_the_start()
    {
        var settings = Production("clamav");
        settings["Workforce:Certificates:Scanner:Port"] = "0";

        Should.Throw<InvalidOperationException>(() => Build("Production", settings));
    }
}
