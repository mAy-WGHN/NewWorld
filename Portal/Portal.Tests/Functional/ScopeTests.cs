using System.Net;
using System.Text.Json;
using Portal.Authentication;
using Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Portal.Tests.Functional;

/// <summary>Scopes: Rollen-Rechte löst je Recht die Reichweite (Filialen/Einzelmeldungen) auf, das Portal wendet sie auf Meldungen und Statistik an — samt den vordefinierten 403-Meldungen. Heimatfiliale, Gültigkeit und Delegation prüft Rollen-Rechte selbst.</summary>
public class ScopeTests(ApiFactory factory) : ApiTestBase(factory)
{
    // --- Hilfen ----------------------------------------------------------------------------------------------

    private async Task<List<Guid>> ReportIdsAsync(string token, string marker)
    {
        var response = await GetAsync($"/api/reports?search={marker}&limit=100", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await DataAsync(response)).GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
    }

    private static string Marker() => "m" + Guid.NewGuid().ToString("N")[..12];

    private static async Task<string?> MessageAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString();

    // --- Meldungen: Sichtbarkeit -----------------------------------------------------------------------------

    [Fact]
    public async Task Reports_AScopedUserSeesOnlyReportsOfHisLocations_AndOneLocationIsEnough()
    {
        var (a, b, c) = (await CreateLocationAsync(), await CreateLocationAsync(), await CreateLocationAsync());
        var marker = Marker();
        var inA = await CreateReportAsync(a, description: marker);
        var inAandB = await CreateReportAsync(b, description: marker, extraLocations: [a]);
        var inB = await CreateReportAsync(b, description: marker);
        var inC = await CreateReportAsync(c, description: marker);
        var (_, token) = await ScopedUserAsync(["report:read"], [a.Id]);
        var (_, admin) = await AdminWithTokenAsync();

        var visible = await ReportIdsAsync(token, marker);

        Assert.Equal(new[] { inA.Id, inAandB.Id }.Order(), visible.Order());
        Assert.Equal(4, (await ReportIdsAsync(admin, marker)).Count);
        Assert.DoesNotContain(inB.Id, visible);
        Assert.DoesNotContain(inC.Id, visible);
    }

    [Fact]
    public async Task Reports_ARegionScopeWithSeveralLocations_CoversAllOfThem()
    {
        var locations = new[] { await CreateLocationAsync(), await CreateLocationAsync(), await CreateLocationAsync() };
        var outside = await CreateLocationAsync();
        var marker = Marker();
        foreach (var location in locations) await CreateReportAsync(location, description: marker);
        await CreateReportAsync(outside, description: marker);
        var (_, token) = await ScopedUserAsync(["report:read"], locations.Select(l => l.Id).ToArray());

        Assert.Equal(3, (await ReportIdsAsync(token, marker)).Count);
    }

    [Fact]
    public async Task Reports_AnEmptyScopeSeesNothing_FailClosed()
    {
        var a = await CreateLocationAsync();
        var marker = Marker();
        var report = await CreateReportAsync(a, description: marker);
        var (_, token) = await ScopedUserAsync(["report:read"], []);

        Assert.Empty(await ReportIdsAsync(token, marker));
        var detail = await GetAsync($"/api/reports/{report.Id}", token);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
    }

    [Fact]
    public async Task Reports_DetailOutsideTheScope_Returns403WithTheReadMessage_AndUnknownReturns404()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var inA = await CreateReportAsync(a);
        var inB = await CreateReportAsync(b);
        var (_, token) = await ScopedUserAsync(["report:read"], [a.Id]);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/reports/{inA.Id}", token)).StatusCode);
        var outside = await GetAsync($"/api/reports/{inB.Id}", token);
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.Equal(AuthMessages.Read, await MessageAsync(outside));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/reports/{Guid.NewGuid()}", token)).StatusCode);
    }

    [Fact]
    public async Task Reports_ASingleReportScope_GivesAccessToExactlyThatReport()
    {
        var a = await CreateLocationAsync();
        var marker = Marker();
        var shared = await CreateReportAsync(a, description: marker);
        var other = await CreateReportAsync(a, description: marker);
        var (_, token) = await ScopedUserAsync(["report:read"], [], [shared.Id]);

        Assert.Equal([shared.Id], await ReportIdsAsync(token, marker));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/reports/{shared.Id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"/api/reports/{other.Id}", token)).StatusCode);
    }

    // --- Meldungen: Ändern / Löschen -------------------------------------------------------------------------------

    [Fact]
    public async Task Reports_UpdateAndDeleteOutsideTheScope_Return403WithTheMatchingMessage()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var inA = await CreateReportAsync(a);
        var inB = await CreateReportAsync(b);
        var (_, token) = await ScopedUserAsync(["report:update", "report:delete"], [a.Id]);

        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/reports/{inA.Id}", new { comment = "ok" }, token)).StatusCode);
        var update = await PatchAsync($"/api/reports/{inB.Id}", new { comment = "nein" }, token);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(update));
        var delete = await DeleteAsync($"/api/reports/{inB.Id}", token);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(AuthMessages.Delete, await MessageAsync(delete));
        var parent = await DeleteAsync($"/api/reports/{inB.Id}/parent", token);
        Assert.Equal(HttpStatusCode.Forbidden, parent.StatusCode);

        Assert.False(await QueryDbAsync(db => db.Reports.IgnoreQueryFilters().Where(r => r.Id == inB.Id).Select(r => r.Archived).SingleAsync()));
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/reports/{inA.Id}", token)).StatusCode);
    }

    [Fact]
    public async Task Reports_AScopedEditorCannotMoveAReportOutOfHisScope_AndKeepsTheOtherLocations()
    {
        var (a, b, c) = (await CreateLocationAsync(), await CreateLocationAsync(), await CreateLocationAsync());
        var report = await CreateReportAsync(a, extraLocations: [b]);
        var (_, token) = await ScopedUserAsync(["report:update"], [a.Id]);

        // Nicht erlaubt: eine Filiale außerhalb des Scopes hinzufügen.
        var tooMuch = await PatchAsync($"/api/reports/{report.Id}", new { locationIds = new[] { a.Id, c.Id } }, token);
        Assert.Equal(HttpStatusCode.Forbidden, tooMuch.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(tooMuch));

        // Erlaubt: seinen Teil setzen — die unsichtbare Filiale B bleibt erhalten.
        var ok = await PatchAsync($"/api/reports/{report.Id}", new { locationIds = new[] { a.Id } }, token);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var locations = await QueryDbAsync(db => db.Reports.IgnoreQueryFilters().Where(r => r.Id == report.Id)
            .SelectMany(r => r.ReportLocations.Select(l => l.LocationId)).ToListAsync());
        Assert.Equal(new[] { a.Id, b.Id }.Order(), locations.Order());
    }

    // --- Statistik -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Statistics_CountOnlyTheScopedReports()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var month = new DateTime(2004, 2, 10);
        await CreateReportAsync(a, createdAt: month);
        await CreateReportAsync(a, createdAt: month);
        await CreateReportAsync(b, createdAt: month);
        var (_, scoped) = await ScopedUserAsync(["statistic:read"], [a.Id]);
        var (_, admin) = await AdminWithTokenAsync();

        var mine = (await DataAsync(await GetAsync("/api/statistics/monthly?from=2004-02&to=2004-02", scoped))).EnumerateArray().Single();
        var all = (await DataAsync(await GetAsync("/api/statistics/monthly?from=2004-02&to=2004-02", admin))).EnumerateArray().Single();

        Assert.Equal(2, mine.GetProperty("total").GetInt32());
        Assert.True(all.GetProperty("total").GetInt32() >= 3);
    }

    }
