using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Workforce.Core.Certificates;

namespace Workforce.Infrastructure.Certificates;

/// <summary>Where the ClamAV daemon (clamd) listens. Section <c>Workforce:Certificates:Scanner</c>.</summary>
public sealed class ClamAvOptions
{
    public const string Section = "Workforce:Certificates:Scanner";

    /// <summary>Host of clamd. It receives the plain file, so it must be loopback or a private network only this service can reach.</summary>
    public string Host { get; set; } = "";

    public int Port { get; set; } = 3310;

    /// <summary>Upper limit for connecting, sending and waiting for the verdict.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Checks an upload with ClamAV (clamd, <c>INSTREAM</c>): the bytes go over the connection and nowhere else, clamd decides, nothing is
/// stored or logged by this class. Every failure (unreachable, timeout, size limit of clamd, unknown answer) counts as "not clean".
/// </summary>
/// <remarks>
/// Runs on the plain bytes before they are encrypted (the storage only ever sees ciphertext, a scanner there would find nothing).
/// <c>StreamMaxLength</c> of clamd must be at least <c>MaxFileBytes</c>, otherwise clamd cuts the stream and the file counts as not clean.
/// </remarks>
public sealed class ClamAvScanner(ClamAvOptions options, ILogger<ClamAvScanner> logger) : IMalwareScanner
{
    private const int ChunkSize = 64 * 1024;

    public async Task<bool> IsCleanAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            using var client = new TcpClient();
            await client.ConnectAsync(options.Host, options.Port, timeout.Token);
            var stream = client.GetStream();

            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var length = new byte[4];
            for (var offset = 0; offset < content.Length; offset += ChunkSize)
            {
                var chunk = content.Slice(offset, Math.Min(ChunkSize, content.Length - offset));
                BinaryPrimitives.WriteUInt32BigEndian(length, (uint)chunk.Length);
                await stream.WriteAsync(length, timeout.Token);
                await stream.WriteAsync(chunk, timeout.Token);
            }

            await stream.WriteAsync(new byte[4], timeout.Token); // zero-length chunk ends the stream
            await stream.FlushAsync(timeout.Token);

            var reply = await ReadReplyAsync(stream, timeout.Token);
            if (reply == "stream: OK")
            {
                return true;
            }

            if (reply.EndsWith(" FOUND", StringComparison.Ordinal))
            {
                // The signature name describes the malware, not the person or the document.
                Log.MalwareFound(logger, reply["stream: ".Length..], null);
            }
            else
            {
                Log.ScannerError(logger, null);
            }

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Unreachable, timed out, connection cut by clamd: not clean (fail closed). The exception text carries no file content.
            Log.ScannerUnreachable(logger, ex);
            return false;
        }
    }

    /// <summary>Asks clamd <c>PING</c>; true on <c>PONG</c>. For the health check only, sends no content.</summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var client = new TcpClient();
            await client.ConnectAsync(options.Host, options.Port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync("zPING\0"u8.ToArray(), timeout.Token);
            return await ReadReplyAsync(stream, timeout.Token) == "PONG";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static class Log
    {
        // The signature name describes the malware, not the person or the document; no content is ever logged.
        public static readonly Action<ILogger, string, Exception?> MalwareFound =
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, nameof(MalwareFound)), "Malware scanner rejected an upload: {Verdict}");

        public static readonly Action<ILogger, Exception?> ScannerError =
            LoggerMessage.Define(LogLevel.Error, new EventId(2, nameof(ScannerError)),
                "Malware scanner returned an error, upload treated as not clean (check StreamMaxLength of clamd)");

        public static readonly Action<ILogger, Exception?> ScannerUnreachable =
            LoggerMessage.Define(LogLevel.Error, new EventId(3, nameof(ScannerUnreachable)), "Malware scanner not reachable, upload treated as not clean");
    }

    private static async Task<string> ReadReplyAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (n == 0)
            {
                break;
            }

            read += n;
            if (buffer.AsSpan(0, read).Contains((byte)0))
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, read).Trim('\0', '\n', '\r', ' ');
    }
}

/// <summary>Degraded (not unhealthy) when clamd does not answer: the service stays up, uploads are refused until the scanner is back.</summary>
public sealed class ClamAvHealthCheck(ClamAvScanner scanner) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await scanner.PingAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Malware scanner does not answer; certificate uploads are refused.");
}
