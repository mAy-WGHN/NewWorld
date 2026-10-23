using Notifications.Contracts;
using Notifications.Core.Delivery;
using Notifications.Core.Templates;
using Microsoft.Extensions.Time.Testing;
using static Notifications.Contracts.NotificationChannel;
using static Notifications.Core.Tests.Samples;

namespace Notifications.Core.Tests;

public class DispatcherTests
{
    private readonly FakeNotificationStore _store = new();
    private readonly FakePreferences _preferences = new();
    private readonly FakePush _push = new();
    private readonly FakeEmail _email = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2028, 2, 29, 23, 30, 0, TimeSpan.Zero));

    private NotificationDispatcher Dispatcher() => new(Catalog(), _store, _preferences, _push, _email, _time);

    [Fact]
    public async Task In_app_notification_is_stored_with_the_rendered_text_and_the_deep_link()
    {
        await Dispatcher().DispatchAsync(Command(), CancellationToken.None);

        var stored = _store.InApp.ShouldHaveSingleItem();
        stored.EmployeeId.ShouldBe(Max);
        stored.Title.ShouldBe("Krankmeldung");
        stored.Body.ShouldBe("Erika Mustermann hat sich ab 2026-03-30 krank gemeldet.");
        stored.DeepLink.ShouldBe("/absences/0197a000-0000-7000-8000-000000000201");
        stored.CreatedAt.ShouldBe(_time.GetUtcNow());
        stored.ReadAt.ShouldBeNull();
        _store.Deliveries.ShouldHaveSingleItem().Status.ShouldBe(DeliveryStatus.Sent);
    }

    [Fact]
    public async Task Values_are_inserted_literally_and_never_expanded_again()
    {
        var data = new Dictionary<string, string> { ["employeeName"] = "{from}", ["from"] = "2026-03-30" };

        await Dispatcher().DispatchAsync(Command(data: data), CancellationToken.None);

        _store.InApp.Single().Body.ShouldBe("{from} hat sich ab 2026-03-30 krank gemeldet.");
    }

    [Fact]
    public async Task The_same_notification_id_creates_exactly_one_notification_per_recipient()
    {
        var id = Guid.NewGuid();

        await Dispatcher().DispatchAsync(Command(notificationId: id), CancellationToken.None);
        await Dispatcher().DispatchAsync(Command(notificationId: id), CancellationToken.None); // redelivery with a new message id
        await Dispatcher().DispatchAsync(Command(notificationId: id, recipients: [Max, Sabine]), CancellationToken.None); // adds only the new recipient

        _store.InApp.Select(n => n.EmployeeId).Order().ShouldBe(new[] { Max, Sabine }.Order());
    }

    [Fact]
    public async Task Duplicate_recipients_in_one_command_get_one_notification()
    {
        await Dispatcher().DispatchAsync(Command(recipients: [Max, Max]), CancellationToken.None);

        _store.InApp.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Push_and_mail_receive_only_generic_text_and_the_deep_link_never_data()
    {
        var data = new Dictionary<string, string> { ["headline"] = "Streng geheime Schlagzeile" };

        await Dispatcher().DispatchAsync(Command("portal.news.employee", [InApp, Push, Email], data), CancellationToken.None);

        var push = _push.Sent.ShouldHaveSingleItem();
        var email = _email.Sent.ShouldHaveSingleItem();
        push.ShouldBe(new GenericMessage(Max, "Neue Mitteilung im Portal", "Öffnen, um die Mitteilung zu lesen.", "/absences/0197a000-0000-7000-8000-000000000201"));
        email.Title.ShouldBe("Neue Mitteilung im Portal");
        foreach (var text in new[] { push.Title, push.Body, email.Title, email.Body })
        {
            text.ShouldNotContain("geheime");
        }

        _store.InApp.Single().Body.ShouldBe("Streng geheime Schlagzeile");
    }

    [Fact]
    public async Task Rejected_command_stores_and_sends_nothing_and_reports_the_reason_code()
    {
        var ex = await Should.ThrowAsync<NotificationRejectedException>(() =>
            Dispatcher().DispatchAsync(Command(channels: [InApp, Push]), CancellationToken.None));

        ex.Reason.ShouldBe(Rejection.SensitiveDataOnNonInAppChannel);
        ex.Message.ShouldNotContain("Erika");
        _store.InApp.ShouldBeEmpty();
        _push.Sent.ShouldBeEmpty();
        _store.Deliveries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_channel_is_skipped_for_optional_templates_and_recorded()
    {
        _preferences.Set(Max, "portal.news.employee", Push, enabled: false);

        await Dispatcher().DispatchAsync(Command("portal.news.employee", [InApp, Push], new Dictionary<string, string> { ["headline"] = "x" }), CancellationToken.None);

        _push.Sent.ShouldBeEmpty();
        _store.InApp.ShouldHaveSingleItem();
        _store.Deliveries.Single(d => d.Channel == Push).Status.ShouldBe(DeliveryStatus.SkippedByPreference);
    }

    [Fact]
    public async Task Preferences_are_per_person_type_and_channel()
    {
        _preferences.Set(Max, "portal.news.employee", InApp, enabled: false);
        var data = new Dictionary<string, string> { ["headline"] = "x" };

        await Dispatcher().DispatchAsync(Command("portal.news.employee", [InApp, Push], data, recipients: [Max, Sabine]), CancellationToken.None);

        _store.InApp.Select(n => n.EmployeeId).ShouldBe([Sabine]);
        _push.Sent.Select(p => p.EmployeeId).Order().ShouldBe(new[] { Max, Sabine }.Order());
    }

    [Fact]
    public async Task Mandatory_templates_ignore_preferences()
    {
        _preferences.Set(Max, "workforce.sick-note.store-manager", InApp, enabled: false);

        await Dispatcher().DispatchAsync(Command(), CancellationToken.None);

        _store.InApp.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Adapter_failure_is_recorded_without_losing_the_in_app_notification_or_the_other_recipients()
    {
        _push.Fail = true;

        await Dispatcher().DispatchAsync(Command("portal.news.employee", [InApp, Push], new Dictionary<string, string> { ["headline"] = "x" }, recipients: [Max, Sabine]), CancellationToken.None);

        _store.InApp.Count.ShouldBe(2);
        _store.Deliveries.Where(d => d.Channel == Push).ShouldAllBe(d => d.Status == DeliveryStatus.Failed);
    }
}
