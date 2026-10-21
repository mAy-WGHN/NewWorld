using System.Net;
using Benachrichtigungen.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Benachrichtigungen.Tests.Functional;

/// <summary>
/// Ereignisse der anderen Anwendungen: Das Portal meldet "Meldung angelegt" (<c>report.created</c>); dieser Dienst entscheidet, wer es erfährt (Rechte laut "Rollen-Rechte",
/// Einstellungen, Wochentag) und stellt per Web Push zu. Übernimmt die Regeln des bisherigen ReportNotifier. Es zählen nur die Empfänger, die der Test selbst angelegt hat.
/// </summary>
public class EventTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static object ReportCreated(string category = "bug", string severity = "medium", Guid? eventId = null, Guid[]? locations = null,
        string description = "Beschreibung", Guid? reportId = null) => new
    {
        id = eventId ?? Guid.NewGuid(),
        type = "report.created",
        occurredAt = DateTime.UtcNow,
        data = new { reportId = reportId ?? Guid.NewGuid(), category, severity, description, locationIds = locations ?? [] }
    };

    /// <summary>Sendet das Ereignis und liefert, welche der übergebenen User einen Push bekamen.</summary>
    private async Task<List<Guid>> NotifiedAsync(object body, params TestUser[] users)
    {
        var response = await SendEventAsync(body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ids = users.Select(u => u.Id).ToHashSet();
        return Factory.PushSender.Sent.SelectMany(s => s.Subscriptions).Select(s => s.UserId).Where(ids.Contains).Distinct().ToList();
    }

    // --- Zugang ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheEndpoint_NeedsTheSendersApiKey()
    {
        var body = ReportCreated();
        var (_, userToken) = NewUserWithToken();

        var missing = await SendEventAsync(body, key: null);
        var wrong = await SendEventAsync(body, key: "falsch");
        var withUserToken = await SendAsync(HttpMethod.Post, "/api/events", body, userToken); // ein Benutzer-Token ersetzt den Absender-Schlüssel nicht

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withUserToken.StatusCode);
        Assert.False((await JsonAsync(missing)).GetProperty("success").GetBoolean());
        Assert.Empty(Factory.PushSender.Sent);
    }

    [Fact]
    public async Task AnUnknownEventType_Returns422()
    {
        var response = await SendEventAsync(new { id = Guid.NewGuid(), type = "gibt.es.nicht", data = new { } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("type", (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task AnIncompleteEnvelope_Returns422()
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendEventAsync(new { type = "report.created", data = new { } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendEventAsync(new { id = Guid.NewGuid(), data = new { } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendEventAsync(new { id = Guid.NewGuid(), type = "report.created" })).StatusCode);
    }

    [Fact]
    public async Task InvalidReportData_Returns422WithFieldNames_AndIsNotRecorded()
    {
        var id = Guid.NewGuid();

        var response = await SendEventAsync(new { id, type = "report.created", data = new { category = "bug" } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var fields = (await JsonAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("data.reportId", fields);
        Assert.Contains("data.severity", fields);
        Assert.False(await QueryDbAsync(db => db.ProcessedEvents.AnyAsync(e => e.Id == id)));
    }

    [Fact]
    public async Task AnUnknownCategory_Returns422()
    {
        var response = await SendEventAsync(ReportCreated(category: "erfunden"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- Kategorie-/Schwere-Matrix (wie bisher im Portal) ----------------------------------------------------------------

    [Fact]
    public async Task Virus_AlwaysNotifies_EvenWhenEverythingElseIsOff()
    {
        var user = await RecipientAsync(s =>
        {
            s.BugNotificationsEnabled = false;
            s.TheftNotificationsEnabled = false;
        });

        Assert.Equal([user.Id], await NotifiedAsync(ReportCreated("virus", "low"), user));
    }

    [Theory]
    [InlineData("low", false)]
    [InlineData("medium", false)]
    [InlineData("high", true)]
    [InlineData("critical", true)]
    public async Task Bug_NotifiesFromTheDefaultThresholdHigh(string severity, bool expected)
    {
        var user = await RecipientAsync();

        Assert.Equal(expected, (await NotifiedAsync(ReportCreated("bug", severity), user)).Count == 1);
    }

    [Fact]
    public async Task Bug_WithALoweredThreshold_NotifiesForLowerSeverities()
    {
        var user = await RecipientAsync(s => s.BugNotificationMinSeverity = Domain.Enums.ReportSeverity.Low);

        Assert.Single(await NotifiedAsync(ReportCreated("bug", "low"), user));
    }

    [Fact]
    public async Task Bug_WithBugNotificationsDisabled_DoesNotNotify_EvenForCritical_ButVirusStillDoes()
    {
        var user = await RecipientAsync(s => s.BugNotificationsEnabled = false);

        Assert.Empty(await NotifiedAsync(ReportCreated("bug", "critical"), user));
        Assert.Single(await NotifiedAsync(ReportCreated("virus", "critical"), user));
    }

    [Theory]
    [InlineData("theft")]
    [InlineData("other")]
    [InlineData("suggestion")]
    public async Task TheftOtherSuggestion_AreOffByDefault(string category)
    {
        var user = await RecipientAsync();

        Assert.Empty(await NotifiedAsync(ReportCreated(category, "critical"), user));
    }

    [Theory]
    [InlineData("theft")]
    [InlineData("other")]
    [InlineData("suggestion")]
    public async Task TheftOtherSuggestion_NotifyWhenEnabled(string category)
    {
        var user = await RecipientAsync(s =>
        {
            s.TheftNotificationsEnabled = category == "theft";
            s.OtherNotificationsEnabled = category == "other";
            s.SuggestionNotificationsEnabled = category == "suggestion";
        });

        Assert.Single(await NotifiedAsync(ReportCreated(category, "low"), user));
    }

    // --- Wochentage -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task WeekendOnlyUser_IsSkippedOnWeekdays_ButNotifiedOnTheWeekend()
    {
        var user = await RecipientAsync(s => s.SetNotificationWeekdays([6, 7]));

        Assert.Empty(await NotifiedAsync(ReportCreated("virus"), user));
        Factory.Clock.SetUtcNow(ApiFactory.Saturday);
        Assert.Single(await NotifiedAsync(ReportCreated("virus"), user));
    }

    [Fact]
    public async Task UserWithNoWeekdaysAtAll_NeverGetsAPush()
    {
        var user = await RecipientAsync(s => s.SetNotificationWeekdays([]));

        Assert.Empty(await NotifiedAsync(ReportCreated("virus"), user));
    }

    [Fact]
    public async Task TheWeekday_IsDeterminedInEuropeBerlin_NotInUtc()
    {
        // Sonntag 22:30 UTC = Montag 00:30 in Berlin (Sommerzeit, UTC+2).
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 11, 22, 30, 0, TimeSpan.Zero));
        var mondayOnly = await RecipientAsync(s => s.SetNotificationWeekdays([1]));
        var sundayOnly = await RecipientAsync(s => s.SetNotificationWeekdays([7]));

        var notified = await NotifiedAsync(ReportCreated("virus"), mondayOnly, sundayOnly);

        Assert.Equal([mondayOnly.Id], notified);
    }

    // --- Empfängerauswahl ----------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyUsersWhoMaySeeTheReport_AreNotified_EvenWithEverythingSwitchedOn()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var allowed = await RecipientAsync();
        var forbidden = await RecipientAsync(s => s.TheftNotificationsEnabled = true, allowed: false);

        var notified = await NotifiedAsync(ReportCreated("virus", locations: [a, b]), allowed, forbidden);

        Assert.Equal([allowed.Id], notified);
    }

    [Fact]
    public async Task RolleRechte_IsAskedForReportReadWithTheLocationsAndTheReport_AndOnlyForUsersWithADevice()
    {
        var (a, b, reportId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var withDevice = await RecipientAsync();
        Factory.Recipients.Allowed.Add(Guid.NewGuid()); // jemand ohne Gerät: wird gar nicht erst gefragt

        await NotifiedAsync(ReportCreated("virus", locations: [a, b], reportId: reportId), withDevice);

        var call = Assert.Single(Factory.Recipients.Calls);
        Assert.Equal("report:read", call.Privilege);
        Assert.Equal(new[] { a, b }.Order(), call.LocationIds.Order());
        Assert.Equal([reportId], call.ObjectIds);
        Assert.Contains(withDevice.Id, call.Candidates);
    }

    [Fact]
    public async Task AllDevicesOfAnAllowedUser_GetThePush()
    {
        var user = await RecipientAsync(devices: 3);

        await NotifiedAsync(ReportCreated("virus"), user);

        Assert.Equal(3, Factory.PushSender.Sent.SelectMany(s => s.Subscriptions).Count(s => s.UserId == user.Id));
    }

    [Fact]
    public async Task AUserWithoutADevice_IsNeverNotified()
    {
        var noDevice = new TestUser(Guid.NewGuid(), "ohne-geraet");
        Factory.Recipients.Allowed.Add(noDevice.Id);

        Assert.Empty(await NotifiedAsync(ReportCreated("virus"), noDevice));
    }

    // --- Inhalt -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheMessage_HasTheTitleOfTheCategory_TheDescriptionAsBody_AndALinkToTheCategory()
    {
        var user = await RecipientAsync();

        await NotifiedAsync(ReportCreated("bug", "critical", description: "Kasse 3 ist ausgefallen"), user);
        await NotifiedAsync(ReportCreated("virus", description: "Verdächtige Mail"), user);

        var bug = Factory.PushSender.Sent.Single(s => s.Body == "Kasse 3 ist ausgefallen");
        Assert.Equal("Neue Meldung: Bug (critical)", bug.Title);
        Assert.Equal($"{ApiFactory.FrontendBaseUrl}/wghn-admin/meldungen/bug", bug.Url);
        var virus = Factory.PushSender.Sent.Single(s => s.Body == "Verdächtige Mail");
        Assert.Equal("Neue Meldung: Virus-Verdacht", virus.Title);
        Assert.EndsWith("/wghn-admin/meldungen/virus", virus.Url);
    }

    // --- Zuverlässigkeit ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheAnswer_SaysHowManyUsersWereNotified_AndTheEventIsRecordedWithItsSource()
    {
        var user = await RecipientAsync();
        var id = Guid.NewGuid();

        var response = await SendEventAsync(ReportCreated("virus", eventId: id));

        var data = await DataAsync(response);
        Assert.False(data.GetProperty("duplicate").GetBoolean());
        Assert.True(data.GetProperty("recipients").GetInt32() >= 1);
        var stored = await QueryDbAsync(db => db.ProcessedEvents.AsNoTracking().SingleAsync(e => e.Id == id));
        Assert.Equal("report.created", stored.Type);
        Assert.Equal(ApiFactory.Source, stored.Source);
        Assert.NotNull(user);
    }

    [Fact]
    public async Task TheSameEventTwice_IsProcessedOnce()
    {
        var user = await RecipientAsync();
        var body = ReportCreated("virus", eventId: Guid.NewGuid());

        var first = await SendEventAsync(body);
        var second = await SendEventAsync(body);

        Assert.False((await DataAsync(first)).GetProperty("duplicate").GetBoolean());
        var again = await DataAsync(second);
        Assert.True(again.GetProperty("duplicate").GetBoolean());
        Assert.Equal(0, again.GetProperty("recipients").GetInt32());
        Assert.Equal(1, Factory.PushSender.Sent.Count(s => s.Subscriptions.Any(x => x.UserId == user.Id)));
    }

    [Fact]
    public async Task WhenRolleRechteIsDown_Returns503_AndTheEventCanBeSentAgainLater()
    {
        var user = await RecipientAsync();
        var body = ReportCreated("virus", eventId: Guid.NewGuid());
        Factory.Recipients.Unavailable = true;

        var down = await SendEventAsync(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Empty(Factory.PushSender.Sent);

        Factory.Recipients.Unavailable = false;
        var retry = await SendEventAsync(body);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.False((await DataAsync(retry)).GetProperty("duplicate").GetBoolean());
        Assert.Single(Factory.PushSender.Sent.Where(s => s.Subscriptions.Any(x => x.UserId == user.Id)));
    }

    [Fact]
    public async Task AFailingPushDelivery_IsNotAnError_AndTheEventCountsAsProcessed()
    {
        await RecipientAsync();
        Factory.PushSender.ThrowOnSend = new InvalidOperationException("Push-Dienst kaputt");
        var id = Guid.NewGuid();

        var response = await SendEventAsync(ReportCreated("virus", eventId: id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await QueryDbAsync(db => db.ProcessedEvents.AnyAsync(e => e.Id == id)));
    }
}
