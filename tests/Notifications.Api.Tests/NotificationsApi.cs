using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Notifications.Api;
using Notifications.Contracts;
using Notifications.Core;
using Notifications.Infrastructure;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;

namespace Notifications.Api.Tests;

/// <summary>The real Notifications API on its own database; commands arrive through the message dispatcher like from the queue.</summary>
public sealed class NotificationsApi : WebApplicationFactory<ApiMarker>
{
    public static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    private readonly string _connectionString;
    private readonly ILoggerProvider _logs;

    private NotificationsApi(string connectionString, ILoggerProvider logs)
    {
        _connectionString = connectionString;
        _logs = logs;
    }

    public HttpClient Http { get; private set; } = null!;

    public static async Task<NotificationsApi> StartAsync(PostgresFixture postgres, ILoggerProvider? logs = null)
    {
        var api = new NotificationsApi(await postgres.CreateDatabaseAsync(), logs ?? new NullLoggerProvider());
        api.Http = api.CreateClient();
        var cache = api.Services.GetRequiredService<IPermissionCache>();
        foreach (var person in new[] { Max, Erika })
        {
            cache.Grant(person, NotificationPrivileges.All.Select(k => new TestGrant(k)).ToArray());
        }

        return api;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddTestAuthentication();
            services.AddLogging(b => b.AddProvider(_logs));
        });
    }

    public async Task<DispatchResult> DeliverAsync(SendNotification command) =>
        await Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(command.MessageId.ToString(), nameof(SendNotification), MessageJson.Serialize(command), 1), CancellationToken.None);

    public static SendNotification Command(
        Guid? notificationId = null, string template = "workforce.sick-note.store-manager", IReadOnlyList<NotificationChannel>? channels = null,
        IReadOnlyList<Guid>? recipients = null, IReadOnlyDictionary<string, string>? data = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, notificationId ?? Guid.NewGuid(), recipients ?? [Max], template,
        data ?? new Dictionary<string, string> { ["employeeName"] = "Erika Mustermann", ["from"] = "2026-03-30" },
        channels ?? [NotificationChannel.InApp], "/absences/0197a000-0000-7000-8000-000000000201");

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid? person, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (person is { } p)
        {
            request.AsUser(p);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Http.SendAsync(request);
    }

    public async Task<T> GetAsync<T>(string url, Guid person)
    {
        using var response = await SendAsync(HttpMethod.Get, url, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Http?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class NullLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }
    }
}
