using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core;
using Portal.Core.Reports;
using Portal.Infrastructure;
using Portal.Infrastructure.Reports;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// Reports (incident reports) through the real API and database, compatible with the existing frontend. Creating is public and rate limited;
/// everything else needs a privilege and is limited to the stores the person reaches ("one store is enough"). Clock: 2026-10-23, 12:00 UTC.
/// </summary>
public sealed class ReportEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static object Form(
        Guid[]? stores = null, string category = "bug", string severity = "medium", string description = "Kasse 2 hängt", int personnelNumber = 4711) =>
        new { personnelNumber, category, severity, description, locationIds = stores ?? [StoreA] };

    private static Guid Id(JsonElement report) => report.GetProperty("id").GetGuid();

    /// <summary>A report written straight into the database (to have other creation times, status and so on).</summary>
    private static async Task<Guid> SeedAsync(
        PortalApi api, DateTime? createdAt = null, ReportCategory category = ReportCategory.Bug, ReportSeverity severity = ReportSeverity.Medium,
        ReportStatus status = ReportStatus.Open, ReportSource source = ReportSource.Employee, bool archived = false, int personnelNumber = 1, string description = "Text", params Guid[] stores)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var created = createdAt ?? Start.UtcDateTime;
        var record = new ReportRecord
        {
            PersonnelNumber = personnelNumber, Category = category, Severity = severity, Status = status, Source = source, Archived = archived,
            Description = description, CreatedAt = created, UpdatedAt = created,
        };
        foreach (var store in stores.Length == 0 ? [StoreA] : stores)
        {
            record.Locations.Add(new ReportLocationRecord { LocationId = store });
        }

        db.Reports.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }

    private static async Task<List<Guid>> ListIdsAsync(PortalApi api, string url, Guid person)
    {
        using var response = await api.SendAsync(HttpMethod.Get, url, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("items").EnumerateArray().Select(Id).ToList();
    }

    // ------------------------------------------------------------------ creating (public)

    [Fact]
    public async Task Anyone_can_file_a_report_it_is_open_from_the_employee_and_has_the_stores_with_names_sorted_by_store_number()
    {
        await using var api = await StartAsync(postgres);

        // the client claims "system" and sends a store twice: the server decides the source and ignores the duplicate
        using var response = await api.SendAsync(HttpMethod.Post, "/api/reports", person: null, new
        {
            personnelNumber = 4711, source = "system", category = "theft", severity = "high", description = "Kasse fehlt",
            locationIds = new[] { StoreB, StoreA, StoreB }, device = "Kasse 2", noticedAt = "2026-10-22T08:30:00", noticedHow = "Beim Öffnen", priorActivity = "Inventur",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var report = await DataAsync(response);
        report.GetProperty("personnelNumber").GetInt32().ShouldBe(4711);
        report.GetProperty("status").GetString().ShouldBe("open");
        report.GetProperty("source").GetString().ShouldBe("employee");
        report.GetProperty("category").GetString().ShouldBe("theft");
        report.GetProperty("severity").GetString().ShouldBe("high");
        report.GetProperty("archived").GetBoolean().ShouldBeFalse();
        report.GetProperty("comment").ValueKind.ShouldBe(JsonValueKind.Null);
        report.GetProperty("parentReportId").ValueKind.ShouldBe(JsonValueKind.Null);
        report.GetProperty("childReportIds").GetArrayLength().ShouldBe(0);
        report.GetProperty("device").GetString().ShouldBe("Kasse 2");
        report.GetProperty("noticedAt").GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("2026-10-22 08:30:00");
        report.GetProperty("createdAt").GetDateTimeOffset().ShouldBe(Start);
        var locations = report.GetProperty("locations").EnumerateArray().ToList();
        locations.Select(l => l.GetProperty("storeCode").GetString()).ShouldBe(["017", "018"]);
        locations[0].GetProperty("name").GetString().ShouldBe("Store A");
        locations[0].GetProperty("archived").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData("personnelNumber")]
    [InlineData("category")]
    [InlineData("severity")]
    [InlineData("description")]
    [InlineData("locationIds")]
    public async Task Missing_required_fields_are_a_422_with_the_field(string field)
    {
        await using var api = await StartAsync(postgres);
        var body = new Dictionary<string, object?>
        {
            ["personnelNumber"] = 4711, ["category"] = "bug", ["severity"] = "low", ["description"] = "x", ["locationIds"] = new[] { StoreA },
        };
        body.Remove(field);

        using var response = await api.SendAsync(HttpMethod.Post, "/api/reports", person: null, body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errors = (await ErrorAsync(response)).GetProperty("errors").EnumerateArray().ToList();
        errors.Single().GetProperty("field").GetString().ShouldBe(field);
        errors.Single().GetProperty("message").GetString().ShouldBe("Dieser Wert darf nicht leer sein.");
    }

    [Fact]
    public async Task Invalid_values_are_refused_with_the_field_and_a_German_message()
    {
        await using var api = await StartAsync(postgres);

        using var noStore = await api.SendAsync(HttpMethod.Post, "/api/reports", null, Form(stores: []));
        using var badNumber = await api.SendAsync(HttpMethod.Post, "/api/reports", null, Form(personnelNumber: 0));
        using var longDevice = await api.SendAsync(HttpMethod.Post, "/api/reports", null, new { personnelNumber = 1, category = "bug", severity = "low", description = "x", locationIds = new[] { StoreA }, device = new string('x', 121) });
        using var badCategory = await api.SendAsync(HttpMethod.Post, "/api/reports", null, Form(category: "kaputt"));

        foreach (var response in new[] { noStore, badNumber, longDevice, badCategory })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        (await ErrorAsync(noStore)).GetProperty("errors")[0].GetProperty("message").GetString().ShouldBe("Es muss mindestens eine Filiale angegeben werden.");
        (await ErrorAsync(badNumber)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("personnelNumber");
        (await ErrorAsync(longDevice)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("device");
    }

    [Fact]
    public async Task An_unknown_or_closed_store_is_a_404()
    {
        await using var api = await StartAsync(postgres);

        using var unknown = await api.SendAsync(HttpMethod.Post, "/api/reports", null, Form(stores: [StoreA, Guid.NewGuid()]));
        using var closed = await api.SendAsync(HttpMethod.Post, "/api/reports", null, Form(stores: [StoreD]));

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        closed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorAsync(unknown)).GetProperty("message").GetString().ShouldBe("Eine oder mehrere Filialen wurden nicht gefunden oder sind archiviert.");
        (await api.OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Filing_reports_is_limited_per_client()
    {
        await using var api = await StartAsync(postgres, new() { ["Portal:RateLimiting:ReportsPerMinute"] = "2" });
        await api.ReportAsync(Form());
        await api.ReportAsync(Form());

        using var third = await api.SendAsync(HttpMethod.Post, "/api/reports", null, Form());

        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeGreaterThanOrEqualTo(1);
        (await ErrorAsync(third)).GetProperty("message").GetString().ShouldBe("Zu viele Anfragen, bitte in Kürze erneut versuchen.");
    }

    // ------------------------------------------------------------------ reading and the reach of a person

    [Fact]
    public async Task Reading_needs_the_privilege()
    {
        await using var api = await StartAsync(postgres);
        var id = Id(await api.ReportAsync(Form()));

        using var anonymousList = await api.SendAsync(HttpMethod.Get, "/api/reports", person: null);
        using var deniedList = await api.SendAsync(HttpMethod.Get, "/api/reports", Erika);
        using var deniedDetail = await api.SendAsync(HttpMethod.Get, $"/api/reports/{id}", Erika);
        using var deniedArchived = await api.SendAsync(HttpMethod.Get, "/api/reports/archived", Erika);

        anonymousList.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        deniedList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deniedDetail.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deniedArchived.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorAsync(deniedList)).GetProperty("message").GetString().ShouldBe("Sie haben nicht die erforderliche Berechtigung, diese Inhalte einzusehen.");
    }

    [Fact]
    public async Task A_report_is_seen_by_everybody_who_reaches_at_least_one_of_its_stores()
    {
        await using var api = await StartAsync(postgres);
        var inA = await SeedAsync(api, stores: [StoreA]);
        var inAandB = await SeedAsync(api, stores: [StoreA, StoreB]);
        var inC = await SeedAsync(api, stores: [StoreC]);
        api.Grant(Erika, new TestGrant(PortalPrivileges.ReportRead, [StoreA]));
        api.Grant(Ben, new TestGrant(PortalPrivileges.ReportRead, [StoreB]));

        (await ListIdsAsync(api, "/api/reports", Erika)).ShouldBe([inA, inAandB], ignoreOrder: true);
        (await ListIdsAsync(api, "/api/reports", Ben)).ShouldBe([inAandB]);
        (await ListIdsAsync(api, "/api/reports", Hanna)).ShouldBe([inA, inAandB, inC], ignoreOrder: true);

        using var visible = await api.SendAsync(HttpMethod.Get, $"/api/reports/{inAandB}", Ben);
        using var outside = await api.SendAsync(HttpMethod.Get, $"/api/reports/{inA}", Ben);
        using var unknown = await api.SendAsync(HttpMethod.Get, $"/api/reports/{Guid.NewGuid()}", Ben);

        visible.StatusCode.ShouldBe(HttpStatusCode.OK);
        outside.StatusCode.ShouldBe(HttpStatusCode.Forbidden);   // it exists, but not for this person
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorAsync(unknown)).GetProperty("message").GetString().ShouldStartWith("Der angeforderte Datensatz wurde nicht gefunden");
    }

    // ------------------------------------------------------------------ list: filters, sorting, paging

    [Fact]
    public async Task The_list_is_sorted_by_priority_then_newest_first_and_pages_with_meta()
    {
        await using var api = await StartAsync(postgres);
        var lowOld = await SeedAsync(api, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), severity: ReportSeverity.Low);
        var criticalOld = await SeedAsync(api, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), severity: ReportSeverity.Critical);
        var criticalNew = await SeedAsync(api, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), severity: ReportSeverity.Critical);
        var high = await SeedAsync(api, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), severity: ReportSeverity.High);

        (await ListIdsAsync(api, "/api/reports", Hanna)).ShouldBe([criticalNew, criticalOld, high, lowOld]);
        (await ListIdsAsync(api, "/api/reports?sortOrder=asc", Hanna)).ShouldBe([lowOld, high, criticalOld, criticalNew]);
        (await ListIdsAsync(api, "/api/reports?sortBy=createdAt&sortOrder=desc", Hanna)).ShouldBe([high, criticalNew, criticalOld, lowOld]);

        using var page = await api.SendAsync(HttpMethod.Get, "/api/reports?offset=1&limit=2", Hanna);
        var data = await DataAsync(page);
        data.GetProperty("items").EnumerateArray().Select(Id).ShouldBe([criticalOld, high]);
        var meta = data.GetProperty("meta");
        (meta.GetProperty("total").GetInt32(), meta.GetProperty("offset").GetInt32(), meta.GetProperty("limit").GetInt32()).ShouldBe((4, 1, 2));
    }

    [Fact]
    public async Task The_list_can_be_filtered()
    {
        await using var api = await StartAsync(postgres);
        var theft = await SeedAsync(api, category: ReportCategory.Theft, severity: ReportSeverity.High, personnelNumber: 77, description: "Kasse fehlt", stores: [StoreB]);
        var done = await SeedAsync(api, status: ReportStatus.Done, source: ReportSource.System, description: "Virus gefunden");
        var archived = await SeedAsync(api, archived: true, description: "Alt");
        var october = await SeedAsync(api, new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc), description: "Septemberende");
        var open = await SeedAsync(api, description: "Offen");

        async Task<List<Guid>> By(string query) => await ListIdsAsync(api, "/api/reports?" + query, Hanna);

        (await By("category=theft")).ShouldBe([theft]);
        (await By("severity=high")).ShouldBe([theft]);
        (await By("status=done")).ShouldBe([done]);
        (await By("source=system")).ShouldBe([done]);
        (await By("personnelNumber=77")).ShouldBe([theft]);
        (await By($"locationId={StoreB}")).ShouldBe([theft]);
        (await By("search=VIRUS")).ShouldBe([done]);
        (await By("archived=true")).ShouldBe([archived]);
        (await By("archived=false")).ShouldBe([theft, done, october, open], ignoreOrder: true);
        (await By("month=2026-09")).ShouldBe([october]);
        (await By("day=2026-10-23")).ShouldBe([theft, done, archived, open], ignoreOrder: true);
        (await By("day=2026-09-30&month=2026-10")).ShouldBe([october]); // the day wins over the month
        (await By("category=theft&status=done")).ShouldBeEmpty();
        (await By(string.Empty)).Count.ShouldBe(5); // active and archived ones
    }

    [Theory]
    [InlineData("status=kaputt", "status")]
    [InlineData("category=x", "category")]
    [InlineData("severity=x", "severity")]
    [InlineData("month=2026-13", "month")]
    [InlineData("sortBy=nonsense", "sortBy")]
    [InlineData("sortOrder=up", "sortOrder")]
    [InlineData("limit=201", "limit")]
    [InlineData("limit=0", "limit")]
    [InlineData("offset=-1", "offset")]
    [InlineData("personnelNumber=0", "personnelNumber")]
    public async Task Invalid_filters_are_a_422_naming_the_field(string query, string field)
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Get, "/api/reports?" + query, Hanna);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe(field);
    }

    [Fact]
    public async Task The_archive_lists_only_archived_reports_whatever_the_filter_says()
    {
        await using var api = await StartAsync(postgres);
        var archived = await SeedAsync(api, archived: true);
        await SeedAsync(api);

        (await ListIdsAsync(api, "/api/reports/archived", Hanna)).ShouldBe([archived]);
        (await ListIdsAsync(api, "/api/reports/archived?archived=false", Hanna)).ShouldBe([archived]);
    }

    // ------------------------------------------------------------------ changing

    [Fact]
    public async Task Changing_only_what_is_sent_keeps_the_rest_and_the_source_cannot_change()
    {
        await using var api = await StartAsync(postgres);
        var id = Id(await api.ReportAsync(Form(description: "Alt")));

        using var response = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{id}", Hanna, new { status = "done", comment = "Erledigt", source = "system", severity = "critical" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await DataAsync(response);
        report.GetProperty("status").GetString().ShouldBe("done");
        report.GetProperty("comment").GetString().ShouldBe("Erledigt");
        report.GetProperty("severity").GetString().ShouldBe("critical");
        report.GetProperty("source").GetString().ShouldBe("employee");
        report.GetProperty("description").GetString().ShouldBe("Alt");
        report.GetProperty("category").GetString().ShouldBe("bug");
    }

    [Fact]
    public async Task Changing_needs_the_privilege_and_the_reach_and_an_unknown_report_is_a_404()
    {
        await using var api = await StartAsync(postgres);
        var inA = Id(await api.ReportAsync(Form(stores: [StoreA])));
        api.Grant(Erika, new TestGrant(PortalPrivileges.ReportRead, [StoreA]));
        api.Grant(Ben, new TestGrant(PortalPrivileges.ReportUpdate, [StoreB]));

        using var withoutPrivilege = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{inA}", Erika, new { comment = "x" });
        using var outside = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{inA}", Ben, new { comment = "x" });
        using var unknown = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{Guid.NewGuid()}", Hanna, new { comment = "x" });
        using var anonymous = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{inA}", person: null, new { comment = "x" });

        withoutPrivilege.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorAsync(withoutPrivilege)).GetProperty("message").GetString().ShouldBe("Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu bearbeiten.");
        outside.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var after = await api.SendAsync(HttpMethod.Get, $"/api/reports/{inA}", Hanna);
        (await DataAsync(after)).GetProperty("comment").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_stores_can_be_replaced_and_somebody_with_a_limited_reach_only_changes_their_part()
    {
        await using var api = await StartAsync(postgres);
        var id = Id(await api.ReportAsync(Form(stores: [StoreA, StoreB])));
        api.Grant(Ben, new TestGrant(PortalPrivileges.ReportUpdate, [StoreB, StoreC]), new TestGrant(PortalPrivileges.ReportRead, [StoreB, StoreC]));

        // Ben replaces his part (B) by C; A is not his and stays
        using var limited = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{id}", Ben, new { locationIds = new[] { StoreC } });
        limited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DataAsync(limited)).GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ShouldBe([StoreA, StoreC], ignoreOrder: true);

        // a store outside his reach cannot be named, not even one that is already there
        using var tooFar = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{id}", Ben, new { locationIds = new[] { StoreC, StoreA } });
        tooFar.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var addOutside = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{id}", Hanna, new { locationIds = new[] { StoreA, StoreC } });
        addOutside.StatusCode.ShouldBe(HttpStatusCode.OK);
        var other = Id(await api.ReportAsync(Form(stores: [StoreB])));
        using var forbidden = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{other}", Ben, new { locationIds = new[] { StoreB, StoreA } });
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // without a limit the list is replaced completely
        using var replaced = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{id}", Hanna, new { locationIds = new[] { StoreB } });
        (await DataAsync(replaced)).GetProperty("locations").GetArrayLength().ShouldBe(1);
        using var unknownStore = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{id}", Hanna, new { locationIds = new[] { Guid.NewGuid() } });
        unknownStore.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ parent and child reports

    [Fact]
    public async Task A_report_can_get_a_parent_and_the_parent_lists_its_children_and_the_link_can_be_removed()
    {
        await using var api = await StartAsync(postgres);
        var parent = Id(await api.ReportAsync(Form()));
        var child = Id(await api.ReportAsync(Form()));

        using var linked = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{child}", Hanna, new { parentReportId = parent });
        linked.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DataAsync(linked)).GetProperty("parentReportId").GetGuid().ShouldBe(parent);
        using var parentView = await api.SendAsync(HttpMethod.Get, $"/api/reports/{parent}", Hanna);
        (await DataAsync(parentView)).GetProperty("childReportIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([child]);

        using var removed = await api.SendAsync(HttpMethod.Delete, $"/api/reports/{child}/parent", Hanna);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DataAsync(removed)).GetProperty("parentReportId").ValueKind.ShouldBe(JsonValueKind.Null);
        using var denied = await api.SendAsync(HttpMethod.Delete, $"/api/reports/{child}/parent", Erika);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Parents_must_exist_and_never_form_a_cycle()
    {
        await using var api = await StartAsync(postgres);
        var a = Id(await api.ReportAsync(Form()));
        var b = Id(await api.ReportAsync(Form()));
        var c = Id(await api.ReportAsync(Form()));
        (await api.SendAsync(HttpMethod.Patch, $"/api/reports/{b}", Hanna, new { parentReportId = a })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.SendAsync(HttpMethod.Patch, $"/api/reports/{c}", Hanna, new { parentReportId = b })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var self = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{a}", Hanna, new { parentReportId = a });
        using var cycle = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{a}", Hanna, new { parentReportId = c });
        using var unknown = await api.SendAsync(HttpMethod.Patch, $"/api/reports/{a}", Hanna, new { parentReportId = Guid.NewGuid() });

        self.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        cycle.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(cycle)).GetProperty("message").GetString().ShouldBe("Zyklische Verknüpfung nicht erlaubt.");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorAsync(unknown)).GetProperty("message").GetString().ShouldBe("Hauptreport wurde nicht gefunden.");
    }

    // ------------------------------------------------------------------ archiving

    [Fact]
    public async Task Deleting_archives_the_report_without_a_body_repeatedly_and_it_moves_to_the_archive()
    {
        await using var api = await StartAsync(postgres);
        var id = Id(await api.ReportAsync(Form()));

        using var first = await api.SendAsync(HttpMethod.Delete, $"/api/reports/{id}", Hanna);
        using var second = await api.SendAsync(HttpMethod.Delete, $"/api/reports/{id}", Hanna);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await first.Content.ReadAsStringAsync()).ShouldBeEmpty();
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListIdsAsync(api, "/api/reports/archived", Hanna)).ShouldBe([id]);
        (await ListIdsAsync(api, "/api/reports?archived=false", Hanna)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Archiving_needs_its_own_privilege_and_reach()
    {
        await using var api = await StartAsync(postgres);
        var inA = Id(await api.ReportAsync(Form(stores: [StoreA])));
        api.Grant(Erika, new TestGrant(PortalPrivileges.ReportUpdate, [StoreA]));
        api.Grant(Ben, new TestGrant(PortalPrivileges.ReportDelete, [StoreB]));

        using var updateOnly = await api.SendAsync(HttpMethod.Delete, $"/api/reports/{inA}", Erika);
        using var outside = await api.SendAsync(HttpMethod.Delete, $"/api/reports/{inA}", Ben);
        using var unknown = await api.SendAsync(HttpMethod.Delete, $"/api/reports/{Guid.NewGuid()}", Hanna);

        updateOnly.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorAsync(updateOnly)).GetProperty("message").GetString().ShouldBe("Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu löschen.");
        outside.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
