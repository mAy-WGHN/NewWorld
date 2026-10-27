using System.Net;
using System.Text.Json;
using Shouldly;

namespace AppHost.Tests;

/// <summary>
/// The vertical slice (plan, phase 7): a sick note travels from the Workforce API through Service Bus, BC Integration and the BC
/// fake back to the sync status, and to the store manager's in-app inbox. Real logins (personnel number + PIN), real topology.
/// </summary>
[Trait("Category", "E2E")]
[Collection(AppHostGroup.Name)]
public class AbsenceE2ETests(AppHostFixture fixture)
{
    private const string Workforce = "workforce-api";

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string InTwoDays => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> ReportSickAsync(Landscape landscape, string personnelNumber) =>
        await landscape.PostJsonAsync(Workforce, "/api/me/absences/sick", personnelNumber, new { from = Today, to = InTwoDays });

    private static async Task<string> SyncStatusAsync(Landscape landscape, string personnelNumber, Guid absenceId)
    {
        var absences = await landscape.GetAsync<JsonElement>(Workforce, $"/api/employees/{landscape.EmployeeIdOf(personnelNumber)}/absences", "10043");
        return absences.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == absenceId).GetProperty("syncStatus").GetString()!;
    }

    [Fact]
    public async Task Sick_note_reaches_bc_the_store_manager_and_shows_colleagues_only_absent()
    {
        var landscape = await Landscape.GetAsync(fixture);
        var scenario = await landscape.LoadScenarioAsync("absence-create");
        try
        {
            var absence = await ReportSickAsync(landscape, "10042");
            var absenceId = absence.GetProperty("id").GetGuid();

            // 5. BC fake got an upsert with the absence id as external id.
            var requests = await Landscape.UntilAsync<List<(string Method, string Body, int Status)>>("upsert at the BC fake", async () =>
            {
                var all = await landscape.AbsenceRequestsAsync();
                return all.Any(r => r.Method == "POST" && r.Body.Contains(absenceId.ToString(), StringComparison.OrdinalIgnoreCase)) ? all : null;
            });
            requests.ShouldContain(r => r.Method == "POST" && r.Body.Contains(absenceId.ToString(), StringComparison.OrdinalIgnoreCase));

            // 6. Workforce shows the sync status "Synced".
            await Landscape.UntilAsync("sync status Synced", async () => await SyncStatusAsync(landscape, "10042", absenceId) == "Synced" ? "ok" : null);

            // 7. The store manager has an in-app notification (push-capable texts stay generic: the notification asks for InApp only).
            var inbox = await Landscape.UntilAsync("in-app notification of the store manager", async () =>
            {
                var list = await landscape.GetAsync<JsonElement>("notifications-api", "/api/me/notifications", "10043");
                return list.GetArrayLength() > 0 ? list.EnumerateArray().ToList() : null;
            });
            inbox.ShouldContain(n => n.GetProperty("templateKey").GetString() == "workforce.sick-note.store-manager");

            // 8. A colleague of the same store sees "absent" without a reason; the manager sees the reason.
            var storeUrl = $"/api/stores/{landscape.StoreId}/absences?from={Today}&to={InTwoDays}";
            var colleagueView = (await landscape.GetAsync<JsonElement>(Workforce, storeUrl, "10044")).EnumerateArray()
                .Single(a => a.GetProperty("employeeId").GetGuid() == landscape.EmployeeIdOf("10042"));
            colleagueView.GetProperty("status").GetString().ShouldBe("Absent");
            colleagueView.GetProperty("type").ValueKind.ShouldBe(JsonValueKind.Null);
            var managerView = (await landscape.GetAsync<JsonElement>(Workforce, storeUrl, "10043")).EnumerateArray()
                .Single(a => a.GetProperty("employeeId").GetGuid() == landscape.EmployeeIdOf("10042"));
            managerView.GetProperty("type").GetString().ShouldBe("Sickness");

            // Employees and stores reached the Portal API (event distribution to a third consumer), too.
            (await landscape.QueryAsync("portal", "select count(*) from portal.people"))[0].ShouldBe("6");
            (await landscape.QueryAsync("portal", "select count(*) from portal.stores"))[0].ShouldBe("1");
        }
        finally
        {
            await landscape.UnloadScenarioAsync(scenario);
        }
    }

    [Fact]
    public async Task Throttled_bc_leaves_the_status_pending_and_succeeds_with_the_retry()
    {
        var landscape = await Landscape.GetAsync(fixture);
        var scenario = await landscape.LoadScenarioAsync("absence-throttled-write-e2e");
        try
        {
            var absenceId = (await ReportSickAsync(landscape, "10045")).GetProperty("id").GetGuid();

            await Landscape.UntilAsync("429 from the BC fake", async () =>
                (await landscape.AbsenceRequestsAsync()).Any(r => r.Status == (int)HttpStatusCode.TooManyRequests) ? "ok" : null);
            (await SyncStatusAsync(landscape, "10045", absenceId)).ShouldBe("Pending");

            await Landscape.UntilAsync("sync status Synced after the retry", async () => await SyncStatusAsync(landscape, "10045", absenceId) == "Synced" ? "ok" : null);
            var posts = (await landscape.AbsenceRequestsAsync()).Where(r => r.Method == "POST" && r.Body.Contains(absenceId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
            posts.Count.ShouldBeGreaterThanOrEqualTo(2);
            posts.Select(p => p.Status).ShouldContain(201);
        }
        finally
        {
            await landscape.UnloadScenarioAsync(scenario);
        }
    }

    [Fact]
    public async Task Validation_error_from_bc_fails_the_sync_once_with_a_code_without_personal_data()
    {
        var landscape = await Landscape.GetAsync(fixture);
        var scenario = await landscape.LoadScenarioAsync("absence-validation-error");
        try
        {
            var absenceId = (await ReportSickAsync(landscape, "10046")).GetProperty("id").GetGuid();

            await Landscape.UntilAsync("sync status Failed", async () => await SyncStatusAsync(landscape, "10046", absenceId) == "Failed" ? "ok" : null);
            await Task.Delay(TimeSpan.FromSeconds(5)); // a retry would show up as a second request
            (await landscape.AbsenceRequestsAsync()).Count(r => r.Method == "POST" && r.Body.Contains(absenceId.ToString(), StringComparison.OrdinalIgnoreCase)).ShouldBe(1);

            var failed = await landscape.QueryAsync("bcintegration", $"select payload from bc_integration.outbox_messages where message_type = 'BcSyncFailed' and payload ilike '%{absenceId}%'");
            failed.Count.ShouldBe(1);
            using var document = JsonDocument.Parse(failed[0]);
            document.RootElement.GetProperty("errorCode").GetString().ShouldNotBeNullOrEmpty();
            failed[0].ShouldNotContain("Dora", Case.Insensitive);
            failed[0].ShouldNotContain("secret detail", Case.Insensitive);
            failed[0].ShouldNotContain("10046", Case.Insensitive);
        }
        finally
        {
            await landscape.UnloadScenarioAsync(scenario);
        }
    }
}
