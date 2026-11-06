using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core;
using Portal.Infrastructure;
using Portal.Infrastructure.Messaging;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The audit log of the Portal: filled by the API calls, readable only with its own privilege (everybody else gets <c>404</c> as for an unknown path),
/// and the changed values only with the second privilege.
/// </summary>
public sealed class AuditEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static object Form() => new { personnelNumber = 4711, category = "bug", severity = "medium", description = "Kasse 2 hängt", locationIds = new[] { StoreA } };

    private static async Task<JsonElement> ListAsync(PortalApi api, Guid? person)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/audit", person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Filing_a_report_is_logged_as_an_anonymous_action_with_a_time_from_the_clock()
    {
        await using var api = await StartAsync(postgres);
        await api.ReportAsync(Form());
        api.Grant(Ben, new TestGrant(PortalPrivileges.AuditRead, Unrestricted: true));

        var lines = await ListAsync(api, Ben);

        var report = lines.EnumerateArray().First(l => l.GetProperty("entityType").GetString() == "ReportRecord");
        report.GetProperty("action").GetString().ShouldBe("Create");
        report.GetProperty("actorKind").GetString().ShouldBe("Anonymous");
        report.GetProperty("service").GetString().ShouldBe("portal");
        DateTimeOffset.Parse(report.GetProperty("occurredAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(Start);
    }

    [Fact]
    public async Task A_signed_in_person_is_logged_with_name_and_personnel_number()
    {
        await using var api = await StartAsync(postgres);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var ben = await db.People.FindAsync(Ben);
            ben!.Name = "Ben Beispiel";
            ben.PersonnelNumber = "10043";
            await db.SaveChangesAsync();
        }

        api.Grant(Ben, new TestGrant(PortalPrivileges.AuditRead, Unrestricted: true), new TestGrant(PortalPrivileges.FaqCreate, Unrestricted: true));
        using var created = await api.SendAsync(HttpMethod.Post, "/api/faqs", Ben, new { title = "Frage", description = "Antwort" });
        created.IsSuccessStatusCode.ShouldBeTrue(await created.Content.ReadAsStringAsync());

        var line = (await ListAsync(api, Ben)).EnumerateArray().First(l => l.GetProperty("entityType").GetString() == "FaqRecord");

        line.GetProperty("actorName").GetString().ShouldBe("Ben Beispiel");
        line.GetProperty("actorPersonnelNumber").GetString().ShouldBe("10043");
        line.GetProperty("actorKind").GetString().ShouldBe("Person");
    }

    [Fact]
    public async Task Pages_continue_exactly_after_the_last_line_seen_even_when_lines_share_a_time()
    {
        await using var api = await StartAsync(postgres);
        for (var i = 0; i < 5; i++)
        {
            await api.ReportAsync(Form()); // the clock stands still: every line has the same time
        }

        api.Grant(Ben, new TestGrant(PortalPrivileges.AuditRead, Unrestricted: true));

        var seen = new List<string>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            using var response = await api.SendAsync(HttpMethod.Get, "/api/audit?limit=2" + cursor, Ben);
            var lines = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToList();
            if (lines.Count == 0)
            {
                break;
            }

            seen.AddRange(lines.Select(l => l.GetProperty("id").GetString()!));
            var last = lines[^1];
            cursor = $"&before={Uri.EscapeDataString(last.GetProperty("occurredAt").GetString()!)}&beforeId={last.GetProperty("id").GetString()}";
        }

        var all = (await ListAsync(api, Ben)).EnumerateArray().Select(l => l.GetProperty("id").GetString()!).ToList();
        seen.Count.ShouldBeGreaterThanOrEqualTo(5);
        seen.ShouldBe(all); // same order, nothing twice, nothing missing
    }

    [Fact]
    public async Task Without_the_privilege_the_log_does_not_exist_for_the_caller()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Ben, new TestGrant(PortalPrivileges.StatisticRead, Unrestricted: true));

        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/audit", null);
        using var withoutPrivilege = await api.SendAsync(HttpMethod.Get, "/api/audit", Ben);
        using var unknown = await api.SendAsync(HttpMethod.Get, "/api/does-not-exist", Ben);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        withoutPrivilege.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await withoutPrivilege.Content.ReadAsStringAsync()).ShouldBe(await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Changed_values_are_only_shown_with_the_second_privilege()
    {
        await using var api = await StartAsync(postgres);
        await api.ReportAsync(Form());
        api.Grant(Ben, new TestGrant(PortalPrivileges.AuditRead, Unrestricted: true));
        api.Grant(Hanna, new TestGrant(PortalPrivileges.AuditRead, Unrestricted: true), new TestGrant(PortalPrivileges.AuditReadValues, Unrestricted: true));

        var without = (await ListAsync(api, Ben)).EnumerateArray().First(l => l.GetProperty("entityType").GetString() == "ReportRecord");
        var with = (await ListAsync(api, Hanna)).EnumerateArray().First(l => l.GetProperty("entityType").GetString() == "ReportRecord");

        without.GetProperty("changes").ValueKind.ShouldBe(JsonValueKind.Null);
        with.GetProperty("changes").GetString()!.ShouldContain("Kasse 2");
    }

    [Fact]
    public async Task The_values_privilege_alone_does_not_open_the_log()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Ben, new TestGrant(PortalPrivileges.AuditReadValues, Unrestricted: true));

        using var response = await api.SendAsync(HttpMethod.Get, "/api/audit", Ben);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
