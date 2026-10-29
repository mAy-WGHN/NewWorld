using System.Net;
using System.Text.Json;
using Notifications.Contracts;
using Portal.Core;
using Shared.Messaging;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// Who is told about a new report depends on its category (e.g. the auditor for theft, IT for bugs and viruses) and is set by the administration.
/// The notification carries no content of the report (class 2): the report is opened in the portal.
/// </summary>
public sealed class ReportNotificationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task SetAsync(PortalApi api, string category, params Guid[] employees)
    {
        using var response = await api.SendAsync(HttpMethod.Put, $"/api/report-recipients/{category}", Hanna, new { employeeIds = employees });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static object Form(string category, string description = "Geheimer Text der Meldung") =>
        new { personnelNumber = 4711, category, severity = "high", description, locationIds = new[] { PortalApi.StoreA } };

    private static async Task<List<SendNotification>> SentAsync(PortalApi api) =>
        [.. (await api.OutboxAsync()).Where(m => m.MessageType == nameof(SendNotification)).Select(m => MessageJson.Deserialize<SendNotification>(m.Payload))];

    [Fact]
    public async Task A_new_report_notifies_the_recipients_of_its_category_only()
    {
        await using var api = await StartAsync(postgres);
        await SetAsync(api, "theft", Revisor);
        await SetAsync(api, "bug", It);
        await SetAsync(api, "virus", It, Hanna);

        var theft = await api.ReportAsync(Form("theft"));
        var virus = await api.ReportAsync(Form("virus"));
        await api.ReportAsync(Form("suggestion")); // nobody set: nobody told

        var sent = await SentAsync(api);
        sent.Count.ShouldBe(2);
        var first = sent.Single(n => n.RecipientEmployeeIds.Contains(Revisor));
        first.RecipientEmployeeIds.ShouldBe([Revisor]);
        first.TemplateKey.ShouldBe("portal.report-created.handler");
        first.Channels.ShouldBe([NotificationChannel.InApp, NotificationChannel.Push], ignoreOrder: true);
        first.Data.ShouldBeEmpty();
        first.DeepLink.ShouldBe($"/reports/{theft.GetProperty("id").GetGuid()}");
        first.NotificationId.ShouldBe(theft.GetProperty("id").GetGuid());
        sent.Single(n => n.NotificationId == virus.GetProperty("id").GetGuid()).RecipientEmployeeIds.ShouldBe([It, Hanna], ignoreOrder: true);
    }

    [Fact]
    public async Task The_notification_carries_nothing_of_the_report()
    {
        await using var api = await StartAsync(postgres);
        await SetAsync(api, "theft", Revisor);

        await api.ReportAsync(Form("theft", "Geheimer Text der Meldung"));

        foreach (var message in (await api.OutboxAsync()).Where(m => m.MessageType == nameof(SendNotification)))
        {
            message.Payload.ShouldNotContain("Geheimer", Case.Insensitive);
            message.Payload.ShouldNotContain("4711");
        }
    }

    [Fact]
    public async Task A_report_that_could_not_be_saved_sends_nothing()
    {
        await using var api = await StartAsync(postgres);
        await SetAsync(api, "bug", It);

        using var response = await api.SendAsync(HttpMethod.Post, "/api/reports", null, new { personnelNumber = 1, category = "bug", severity = "low", description = "x", locationIds = new[] { Guid.NewGuid() } });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SentAsync(api)).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ setting the recipients

    [Fact]
    public async Task All_categories_are_listed_also_those_nobody_is_set_for()
    {
        await using var api = await StartAsync(postgres);
        await SetAsync(api, "theft", Revisor);

        using var response = await api.SendAsync(HttpMethod.Get, "/api/report-recipients", Hanna);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = (await DataAsync(response)).EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("category").GetString()).ShouldBe(["virus", "bug", "theft", "other", "suggestion"]);
        rows.Single(r => r.GetProperty("category").GetString() == "theft").GetProperty("employeeIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([Revisor]);
        rows.Single(r => r.GetProperty("category").GetString() == "bug").GetProperty("employeeIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Setting_replaces_the_list_and_an_empty_list_switches_the_notification_off()
    {
        await using var api = await StartAsync(postgres);
        await SetAsync(api, "bug", It, Revisor);
        await SetAsync(api, "bug", It, It); // duplicates count once
        await api.ReportAsync(Form("bug"));
        await SetAsync(api, "bug");
        await api.ReportAsync(Form("bug"));

        var sent = await SentAsync(api);
        sent.Count.ShouldBe(1);
        sent[0].RecipientEmployeeIds.ShouldBe([It]);
    }

    [Fact]
    public async Task Only_known_people_and_known_categories_can_be_set()
    {
        await using var api = await StartAsync(postgres);

        using var unknownPerson = await api.SendAsync(HttpMethod.Put, "/api/report-recipients/bug", Hanna, new { employeeIds = new[] { Guid.NewGuid() } });
        using var unknownCategory = await api.SendAsync(HttpMethod.Put, "/api/report-recipients/kaputt", Hanna, new { employeeIds = Array.Empty<Guid>() });
        using var noList = await api.SendAsync(HttpMethod.Put, "/api/report-recipients/bug", Hanna, new { });

        unknownPerson.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(unknownPerson)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("employeeIds");
        unknownCategory.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noList.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Setting_and_reading_the_recipients_needs_the_privilege()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Erika, TestGrant.Everywhere(PortalPrivileges.ReportRead));

        using var read = await api.SendAsync(HttpMethod.Get, "/api/report-recipients", Erika);
        using var write = await api.SendAsync(HttpMethod.Put, "/api/report-recipients/bug", Erika, new { employeeIds = new[] { It } });
        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/report-recipients", person: null);

        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_person_who_is_erased_is_no_longer_notified()
    {
        await using var api = await StartAsync(postgres);
        await SetAsync(api, "bug", It, Revisor);
        var erasure = new BcIntegration.Contracts.EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, It);
        (await api.DeliverAsync(erasure)).Outcome.ShouldBe(Shared.Infrastructure.Messaging.DispatchOutcome.Completed);

        await api.ReportAsync(Form("bug"));

        (await SentAsync(api)).Single().RecipientEmployeeIds.ShouldBe([Revisor]);
    }
}
