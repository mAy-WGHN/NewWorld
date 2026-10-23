using System.Collections.Concurrent;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Notifications.Api;
using Notifications.Contracts;
using Notifications.Core.Delivery;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Delivery;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using static Notifications.Api.Tests.NotificationsApi;

namespace Notifications.Api.Tests;

/// <summary>The receive path of the plan (phase 5) against PostgreSQL: idempotency, protection rule, generic push, erasure, retention, logs.</summary>
public sealed class DeliveryIntegrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly CaptureLogs _logs = new();

    public void Dispose() => _logs.Dispose();

    private Task<NotificationsApi> Start() => StartAsync(postgres, _logs);

    private static async Task<List<NotificationDto>> Mine(NotificationsApi api, Guid person) =>
        await api.GetAsync<List<NotificationDto>>("/api/me/notifications", person);

    [Fact]
    public async Task Command_creates_an_in_app_notification_that_only_the_recipient_sees()
    {
        await using var api = await Start();

        (await api.DeliverAsync(Command())).Outcome.ShouldBe(DispatchOutcome.Completed);

        var mine = (await Mine(api, Max)).ShouldHaveSingleItem();
        mine.Title.ShouldBe("Krankmeldung");
        mine.Body.ShouldBe("Erika Mustermann hat sich ab 2026-03-30 krank gemeldet.");
        mine.DeepLink.ShouldBe("/absences/0197a000-0000-7000-8000-000000000201");
        (await Mine(api, Erika)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Redelivery_and_a_second_command_with_the_same_notification_id_create_exactly_one_notification()
    {
        await using var api = await Start();
        var id = Guid.NewGuid();
        var first = Command(id);

        await api.DeliverAsync(first);
        (await api.DeliverAsync(first)).Outcome.ShouldBe(DispatchOutcome.Duplicate);   // same message id
        await api.DeliverAsync(Command(id));                                            // new message id, same notification id

        (await Mine(api, Max)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sensitive_data_with_push_is_rejected_to_dead_letter_and_nothing_is_stored_or_logged()
    {
        await using var api = await Start();

        var result = await api.DeliverAsync(Command(channels: [NotificationChannel.InApp, NotificationChannel.Push]));

        result.Outcome.ShouldBe(DispatchOutcome.DeadLetter);
        result.Reason.ShouldBe(nameof(Rejection.SensitiveDataOnNonInAppChannel));
        (await Mine(api, Max)).ShouldBeEmpty();
        api.Services.GetRequiredService<SentMessages>().Items.ShouldBeEmpty();
        _logs.Lines.ShouldContain(l => l.Contains(nameof(Rejection.SensitiveDataOnNonInAppChannel), StringComparison.Ordinal));
        _logs.Lines.ShouldAllBe(l => !l.Contains("Mustermann", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Class_2_data_with_in_app_only_is_accepted()
    {
        await using var api = await Start();

        (await api.DeliverAsync(Command(channels: [NotificationChannel.InApp]))).Outcome.ShouldBe(DispatchOutcome.Completed);
    }

    [Fact]
    public async Task Push_fake_receives_only_generic_text_and_the_deep_link()
    {
        await using var api = await Start();

        await api.DeliverAsync(Command(
            template: "workforce.vacation-decision.requester", channels: [NotificationChannel.Push, NotificationChannel.Email],
            data: new Dictionary<string, string>()));

        var sent = api.Services.GetRequiredService<SentMessages>().Items.ToList();
        sent.Select(s => s.Channel).Order().ShouldBe(["Email", "Push"]);
        sent.ShouldAllBe(s => s.Message.Title == "Neue Mitteilung im Portal" && s.Message.DeepLink == "/absences/0197a000-0000-7000-8000-000000000201");
        sent.ShouldAllBe(s => !s.Message.Body.Contains("Urlaub", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_template_goes_to_dead_letter()
    {
        await using var api = await Start();

        var result = await api.DeliverAsync(Command(template: "workforce.unknown.store-manager"));

        result.Outcome.ShouldBe(DispatchOutcome.DeadLetter);
        result.Reason.ShouldBe(nameof(Rejection.UnknownTemplate));
    }

    [Fact]
    public async Task Disabled_optional_channel_is_skipped_but_mandatory_ones_always_arrive()
    {
        await using var api = await Start();
        using (var scope = api.Services.CreateScope())
        {
            var preferences = scope.ServiceProvider.GetRequiredService<PreferenceService>();
            await preferences.SetAsync(Max, "workforce.vacation-decision.requester", NotificationChannel.InApp, false, CancellationToken.None);
        }

        await api.DeliverAsync(Command(template: "workforce.vacation-decision.requester",
            data: new Dictionary<string, string> { ["decision"] = "genehmigt", ["from"] = "2026-07-01", ["to"] = "2026-07-14" }));
        await api.DeliverAsync(Command());

        (await Mine(api, Max)).ShouldHaveSingleItem().TemplateKey.ShouldBe("workforce.sick-note.store-manager");
        using var check = api.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await db.Deliveries.Select(d => d.Status).ToListAsync()).Order().ShouldBe(["Sent", "SkippedByPreference"]);
    }

    [Fact]
    public async Task Erasure_deletes_everything_about_the_person_and_keeps_others()
    {
        await using var api = await Start();
        await api.DeliverAsync(Command(recipients: [Max, Erika]));
        var erasure = new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, Erika);
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PreferenceService>().SetAsync(Erika, "workforce.vacation-decision.requester", NotificationChannel.Push, false, CancellationToken.None);
        }

        (await api.Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(erasure.MessageId.ToString(), nameof(EmployeeDataErasureRequested), MessageJson.Serialize(erasure), 1), CancellationToken.None))
            .Outcome.ShouldBe(DispatchOutcome.Completed);

        using var check = api.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await db.InApp.Select(n => n.EmployeeId).ToListAsync()).ShouldBe([Max]);
        (await db.Deliveries.Select(d => d.EmployeeId).ToListAsync()).ShouldBe([Max]);
        (await db.Preferences.CountAsync()).ShouldBe(0);
        check.ServiceProvider.GetRequiredService<Shared.Authorization.IPermissionCache>().TryGet(Erika, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Retention_removes_old_in_app_notifications_and_keeps_recent_ones_across_the_leap_day()
    {
        await using var api = await Start();
        await api.DeliverAsync(Command(notificationId: Guid.NewGuid()));
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = new DateTimeOffset(2028, 5, 29, 12, 0, 0, TimeSpan.Zero);
        db.InApp.Add(Old("workforce.sick-note.store-manager", now.AddDays(-91), Max));      // older than 90 days
        db.InApp.Add(Old("workforce.sick-note.store-manager", now.AddDays(-89), Max));      // recent (the period includes 29 Feb 2028)
        await db.SaveChangesAsync();

        var cleaner = new NotificationCleaner(db, new FakeTimeProvider(now), new NotificationOptions { InAppRetention = TimeSpan.FromDays(90) });
        var removed = await cleaner.CleanAsync(CancellationToken.None);

        removed.ShouldBeGreaterThanOrEqualTo(1);
        (await db.InApp.AsNoTracking().Select(n => n.CreatedAt).ToListAsync()).ShouldNotContain(now.AddDays(-91));
        (await db.InApp.AsNoTracking().Select(n => n.CreatedAt).ToListAsync()).ShouldContain(now.AddDays(-89));
    }

    private static Notifications.Infrastructure.Delivery.InAppNotificationRecord Old(string template, DateTimeOffset createdAt, Guid person) => new()
    {
        Id = Guid.NewGuid(), NotificationId = Guid.NewGuid(), EmployeeId = person, TemplateKey = template, Title = "t", Body = "b", CreatedAt = createdAt,
    };

    [Fact]
    public async Task Logs_never_contain_the_data_of_a_notification()
    {
        await using var api = await Start();
        await api.DeliverAsync(Command(data: new Dictionary<string, string> { ["employeeName"] = "Zora Zufallsname", ["from"] = "2026-03-30" }));
        await api.DeliverAsync(Command(channels: [NotificationChannel.Push], data: new Dictionary<string, string> { ["employeeName"] = "Zora Zufallsname" }));

        _logs.Lines.ShouldNotBeEmpty();
        _logs.Lines.ShouldAllBe(l => !l.Contains("Zufallsname", StringComparison.Ordinal));
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(_lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lines.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    lines.Enqueue(exception.ToString());
                }
            }
        }
    }
}
