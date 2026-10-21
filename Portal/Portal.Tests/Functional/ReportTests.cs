using System.Net;
using System.Text.Json;
using Portal.Contracts;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Portal.Tests.Functional;

public class ReportTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static Dictionary<string, object?> Body(LocationResponse location, string category = "bug", string severity = "high",
        int personnelNumber = 4711, string description = "Kasse hängt", Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["personnelNumber"] = personnelNumber,
            ["source"] = "employee",
            ["category"] = category,
            ["severity"] = severity,
            ["description"] = description,
            ["locationIds"] = new[] { location.Id }
        };
        if (extra is not null) foreach (var (k, v) in extra) body[k] = v;
        return body;
    }

    private static List<Guid> Ids(JsonElement data) =>
        data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    // --- Anlegen (öffentlich) ----------------------------------------------------------------------------------

    [Fact]
    public async Task Create_IsPublic_AndReturnsAnOpenEmployeeReport()
    {
        var location = await CreateLocationAsync();

        var response = await PostAsync("/api/reports", Body(location));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal("open", data.GetProperty("status").GetString());
        Assert.Equal("employee", data.GetProperty("source").GetString());
        Assert.Equal("bug", data.GetProperty("category").GetString());
        Assert.Equal("high", data.GetProperty("severity").GetString());
        Assert.Equal(4711, data.GetProperty("personnelNumber").GetInt32());
        Assert.False(data.GetProperty("archived").GetBoolean());
        Assert.Equal(location.Id, data.GetProperty("locations")[0].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("childReportIds").ValueKind);
    }

    [Fact]
    public async Task Create_IgnoresClientSuppliedSource()
    {
        var location = await CreateLocationAsync();

        var data = await DataAsync(await PostAsync("/api/reports",
            Body(location, extra: new() { ["source"] = "system" })));

        Assert.Equal("employee", data.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("virus", "critical")]
    [InlineData("theft", "high")]
    [InlineData("other", "medium")]
    [InlineData("suggestion", "low")]
    public async Task Create_SupportsAllCategories(string category, string severity)
    {
        var location = await CreateLocationAsync();

        var response = await PostAsync("/api/reports", Body(location, category, severity));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(category, (await DataAsync(response)).GetProperty("category").GetString());
    }

    [Fact]
    public async Task Create_AcceptsALongSuggestionDescription()
    {
        var location = await CreateLocationAsync();
        var description = new string('x', 5000);

        var data = await DataAsync(await PostAsync("/api/reports", Body(location, "suggestion", "low", description: description)));

        Assert.Equal(description, data.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Create_StoresOptionalFields_AndKeepsTheClientWallClockTime()
    {
        var location = await CreateLocationAsync();

        var data = await DataAsync(await PostAsync("/api/reports", Body(location, extra: new()
        {
            ["device"] = "Kasse 3",
            ["noticedHow"] = "Fehlermeldung",
            ["priorActivity"] = "Bon gedruckt",
            ["noticedAt"] = "2026-03-05T10:00:00+02:00"
        })));

        Assert.Equal("Kasse 3", data.GetProperty("device").GetString());
        Assert.Equal("Fehlermeldung", data.GetProperty("noticedHow").GetString());
        Assert.Equal("Bon gedruckt", data.GetProperty("priorActivity").GetString());
        // Wie im Symfony-Original: die Wanduhrzeit des Clients wird ohne Umrechnung gespeichert.
        Assert.Equal("2026-03-05T10:00:00+00:00", data.GetProperty("noticedAt").GetString());
    }

    [Fact]
    public async Task Create_CannotSetCommentStatusArchivedOrParent()
    {
        var location = await CreateLocationAsync();
        var parent = await CreateReportAsync(location);

        var data = await DataAsync(await PostAsync("/api/reports", Body(location, extra: new()
        {
            ["comment"] = "geheim",
            ["status"] = "done",
            ["archived"] = true,
            ["parentReportId"] = parent.Id
        })));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("comment").ValueKind);
        Assert.Equal("open", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("archived").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("parentReportId").ValueKind);
    }

    [Fact]
    public async Task Create_WithUnknownLocation_Returns404()
    {
        var location = await CreateLocationAsync();
        var body = Body(location);
        body["locationIds"] = new[] { Guid.NewGuid() };

        var response = await PostAsync("/api/reports", body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Eine oder mehrere Filialen wurden nicht gefunden oder sind archiviert.",
            (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_WithArchivedLocation_Returns404()
    {
        var archived = await CreateLocationAsync(archived: true);

        var response = await PostAsync("/api/reports", Body(archived));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateLocationIds_IsDeduplicated()
    {
        var location = await CreateLocationAsync();
        var body = Body(location);
        body["locationIds"] = new[] { location.Id, location.Id };

        var response = await PostAsync("/api/reports", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, (await DataAsync(response)).GetProperty("locations").GetArrayLength());
    }

    [Fact]
    public async Task Create_WithInvalidBody_Returns422WithFieldErrors()
    {
        var response = await PostAsync("/api/reports", new
        {
            personnelNumber = 0,
            category = "bug",
            severity = "high",
            description = "",
            locationIds = Array.Empty<Guid>()
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var fields = (await JsonAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("personnelNumber", fields);
        Assert.Contains("description", fields);
        Assert.Contains("locationIds", fields);
    }

    [Fact]
    public async Task Create_WithUnknownEnumValue_Returns422()
    {
        var location = await CreateLocationAsync();

        var response = await PostAsync("/api/reports", Body(location, category: "nope"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_IsRateLimitedAfterTenPerMinute()
    {
        var location = await CreateLocationAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++) statuses.Add((await PostAsync("/api/reports", Body(location))).StatusCode);

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.Created, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    // --- Zugriff auf Lesen/Ändern ---------------------------------------------------------------------------------

    [Fact]
    public async Task ListAndDetail_RequireAuthentication()
    {
        var location = await CreateLocationAsync();
        var report = await CreateReportAsync(location);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync($"/api/reports/{report.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_Delete_AndArchiveView_RequireAdmin()
    {
        var location = await CreateLocationAsync();
        var report = await CreateReportAsync(location);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PatchAsync($"/api/reports/{report.Id}", new { status = "done" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync($"/api/reports/{report.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync($"/api/reports/{report.Id}/parent")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/reports/archived")).StatusCode);
    }

    [Fact]
    public async Task Detail_ReturnsAllFields()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var report = await CreateReportAsync(location, ReportCategory.Theft, ReportSeverity.High, 99, "Diebstahl");

        var data = await DataAsync(await GetAsync($"/api/reports/{report.Id}", token));

        foreach (var field in new[]
                 {
                     "id", "personnelNumber", "status", "source", "category", "description", "severity", "locations",
                     "device", "noticedAt", "noticedHow", "priorActivity", "comment", "archived", "parentReportId",
                     "childReportIds", "createdAt", "updatedAt"
                 })
            Assert.True(data.TryGetProperty(field, out _), $"Feld {field} fehlt");
        Assert.Equal("Diebstahl", data.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Detail_OfUnknownId_Returns404()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await GetAsync($"/api/reports/{Guid.NewGuid()}", token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsJsonEnvelope404()
    {
        var response = await GetAsync("/api/reports/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("success").GetBoolean());
    }

    // --- Ändern ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesAllEditableFields()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var report = await CreateReportAsync(location);

        var response = await PatchAsync($"/api/reports/{report.Id}", new
        {
            personnelNumber = 555,
            status = "done",
            category = "other",
            description = "geändert",
            severity = "critical",
            device = "Drucker",
            noticedAt = "2026-01-02T03:04:05+00:00",
            noticedHow = "gesehen",
            priorActivity = "gedruckt",
            comment = "Admin-Notiz"
        }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(555, data.GetProperty("personnelNumber").GetInt32());
        Assert.Equal("done", data.GetProperty("status").GetString());
        Assert.Equal("other", data.GetProperty("category").GetString());
        Assert.Equal("geändert", data.GetProperty("description").GetString());
        Assert.Equal("critical", data.GetProperty("severity").GetString());
        Assert.Equal("Drucker", data.GetProperty("device").GetString());
        Assert.Equal("2026-01-02T03:04:05+00:00", data.GetProperty("noticedAt").GetString());
        Assert.Equal("gesehen", data.GetProperty("noticedHow").GetString());
        Assert.Equal("gedruckt", data.GetProperty("priorActivity").GetString());
        Assert.Equal("Admin-Notiz", data.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task Update_IsPartial_FieldsNotSentStayUnchanged()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var report = await CreateReportAsync(location, description: "Original", personnelNumber: 42);

        var data = await DataAsync(await PatchAsync($"/api/reports/{report.Id}", new { status = "done" }, token));

        Assert.Equal("done", data.GetProperty("status").GetString());
        Assert.Equal("Original", data.GetProperty("description").GetString());
        Assert.Equal(42, data.GetProperty("personnelNumber").GetInt32());
    }

    [Fact]
    public async Task Update_IgnoresSource_BecauseItIsImmutable()
    {
        var (_, token) = await AdminWithTokenAsync();
        var report = await CreateReportAsync(await CreateLocationAsync());

        var data = await DataAsync(await PatchAsync($"/api/reports/{report.Id}", new { source = "system" }, token));

        Assert.Equal("employee", data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Update_LocationIds_ReplacesTheWholeList_AndAnEmptyListClearsIt()
    {
        var (_, token) = await AdminWithTokenAsync();
        var first = await CreateLocationAsync();
        var second = await CreateLocationAsync();
        var report = await CreateReportAsync(first);

        var replaced = await DataAsync(await PatchAsync($"/api/reports/{report.Id}",
            new { locationIds = new[] { second.Id } }, token));
        Assert.Equal(second.Id, replaced.GetProperty("locations").EnumerateArray().Single().GetProperty("id").GetGuid());

        var cleared = await DataAsync(await PatchAsync($"/api/reports/{report.Id}",
            new { locationIds = Array.Empty<Guid>() }, token));
        Assert.Equal(JsonValueKind.Array, cleared.GetProperty("locations").ValueKind);
        Assert.Equal(0, cleared.GetProperty("locations").GetArrayLength());
    }

    [Fact]
    public async Task Update_WithArchivedLocation_Returns404()
    {
        var (_, token) = await AdminWithTokenAsync();
        var report = await CreateReportAsync(await CreateLocationAsync());
        var archived = await CreateLocationAsync(archived: true);

        var response = await PatchAsync($"/api/reports/{report.Id}", new { locationIds = new[] { archived.Id } }, token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_CanRestoreAnArchivedReport()
    {
        var (_, token) = await AdminWithTokenAsync();
        var report = await CreateReportAsync(await CreateLocationAsync(), archived: true);

        var data = await DataAsync(await PatchAsync($"/api/reports/{report.Id}", new { archived = false }, token));

        Assert.False(data.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Update_RefreshesUpdatedAt()
    {
        var (_, token) = await AdminWithTokenAsync();
        var report = await CreateReportAsync(await CreateLocationAsync());
        await QueryDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE reports SET updated_at = '2020-01-01' WHERE id = {report.Id}"));

        var data = await DataAsync(await PatchAsync($"/api/reports/{report.Id}", new { status = "done" }, token));

        Assert.StartsWith(DateTime.UtcNow.Year.ToString(), data.GetProperty("updatedAt").GetString());
    }

    // --- Archivieren -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_ArchivesInsteadOfDeleting_AndIsIdempotent()
    {
        var (_, token) = await AdminWithTokenAsync();
        var report = await CreateReportAsync(await CreateLocationAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/reports/{report.Id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/reports/{report.Id}", token)).StatusCode);

        var stored = await QueryDbAsync(db => db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id));
        Assert.True(stored.Archived);
    }

    [Fact]
    public async Task ArchiveEndpoint_ForcesArchivedRegardlessOfTheQueryParameter()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var active = await CreateReportAsync(location);
        var archived = await CreateReportAsync(location, archived: true);

        var ids = Ids(await DataAsync(await GetAsync($"/api/reports/archived?locationId={location.Id}&archived=false", token)));

        Assert.Equal([archived.Id], ids);
        Assert.DoesNotContain(active.Id, ids);
    }

    // --- Filter ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_FiltersByCategoryStatusSeverityAndPersonnelNumber()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var bug = await CreateReportAsync(location, ReportCategory.Bug, ReportSeverity.High, 1);
        var theft = await CreateReportAsync(location, ReportCategory.Theft, ReportSeverity.Low, 2);
        await Factory.WithDbAsync(async (db, _) =>
        {
            (await db.Reports.FindAsync(theft.Id))!.Status = ReportStatus.Done;
            await db.SaveChangesAsync();
        });
        var baseUrl = $"/api/reports?locationId={location.Id}";

        Assert.Equal([bug.Id], Ids(await DataAsync(await GetAsync($"{baseUrl}&category=bug", token))));
        Assert.Equal([theft.Id], Ids(await DataAsync(await GetAsync($"{baseUrl}&status=done", token))));
        Assert.Equal([bug.Id], Ids(await DataAsync(await GetAsync($"{baseUrl}&severity=high", token))));
        Assert.Equal([theft.Id], Ids(await DataAsync(await GetAsync($"{baseUrl}&personnelNumber=2", token))));
        Assert.Equal(2, Ids(await DataAsync(await GetAsync($"{baseUrl}&source=employee", token))).Count);
        Assert.Empty(Ids(await DataAsync(await GetAsync($"{baseUrl}&source=system", token))));
    }

    [Fact]
    public async Task List_FiltersByLocation()
    {
        var (_, token) = await AdminWithTokenAsync();
        var first = await CreateLocationAsync();
        var second = await CreateLocationAsync();
        var inFirst = await CreateReportAsync(first);
        await CreateReportAsync(second);

        var ids = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={first.Id}", token)));

        Assert.Equal([inFirst.Id], ids);
    }

    [Fact]
    public async Task List_SearchIsCaseInsensitiveInTheDescription()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var match = await CreateReportAsync(location, description: "Der DRUCKER ist kaputt");
        await CreateReportAsync(location, description: "ganz was anderes");

        var ids = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}&search=druCker", token)));

        Assert.Equal([match.Id], ids);
    }

    [Fact]
    public async Task List_FiltersByDay_AndDayWinsOverMonth()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var may10 = await CreateReportAsync(location, createdAt: new DateTime(2020, 5, 10, 8, 0, 0));
        var may11 = await CreateReportAsync(location, createdAt: new DateTime(2020, 5, 11, 9, 0, 0));
        var june = await CreateReportAsync(location, createdAt: new DateTime(2020, 6, 1, 9, 0, 0));
        var baseUrl = $"/api/reports?locationId={location.Id}";

        Assert.Equal([may10.Id], Ids(await DataAsync(await GetAsync($"{baseUrl}&day=2020-05-10", token))));
        Assert.Equal(2, Ids(await DataAsync(await GetAsync($"{baseUrl}&month=2020-05", token))).Count);
        // Month wird ignoriert, wenn Day gesetzt ist.
        Assert.Equal([may10.Id], Ids(await DataAsync(await GetAsync($"{baseUrl}&day=2020-05-10&month=2020-06", token))));
        Assert.Contains(june.Id, Ids(await DataAsync(await GetAsync($"{baseUrl}&month=2020-06", token))));
        Assert.DoesNotContain(may11.Id, Ids(await DataAsync(await GetAsync($"{baseUrl}&month=2020-06", token))));
    }

    [Fact]
    public async Task List_WithoutArchivedParameter_IncludesArchivedReports_ArchivedFalseExcludesThem()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var active = await CreateReportAsync(location);
        var archived = await CreateReportAsync(location, archived: true);
        var baseUrl = $"/api/reports?locationId={location.Id}";

        var all = Ids(await DataAsync(await GetAsync(baseUrl, token)));
        var onlyActive = Ids(await DataAsync(await GetAsync($"{baseUrl}&archived=false", token)));

        Assert.Contains(archived.Id, all);
        Assert.Contains(active.Id, all);
        Assert.Equal([active.Id], onlyActive);
    }

    // --- Sortierung --------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_DefaultSort_IsPriorityThenNewestFirst()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var lowNew = await CreateReportAsync(location, severity: ReportSeverity.Low, createdAt: new DateTime(2021, 1, 3));
        var criticalOld = await CreateReportAsync(location, severity: ReportSeverity.Critical, createdAt: new DateTime(2021, 1, 1));
        var highNew = await CreateReportAsync(location, severity: ReportSeverity.High, createdAt: new DateTime(2021, 1, 4));
        var highOld = await CreateReportAsync(location, severity: ReportSeverity.High, createdAt: new DateTime(2021, 1, 2));

        var ids = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}", token)));

        Assert.Equal([criticalOld.Id, highNew.Id, highOld.Id, lowNew.Id], ids);
    }

    [Fact]
    public async Task List_SortBySeverity_UsesRankNotAlphabeticalOrder()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var low = await CreateReportAsync(location, severity: ReportSeverity.Low);
        var medium = await CreateReportAsync(location, severity: ReportSeverity.Medium);
        var high = await CreateReportAsync(location, severity: ReportSeverity.High);
        var critical = await CreateReportAsync(location, severity: ReportSeverity.Critical);

        var asc = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}&sortBy=severity&sortOrder=asc", token)));
        var desc = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}&sortBy=severity&sortOrder=desc", token)));

        Assert.Equal([low.Id, medium.Id, high.Id, critical.Id], asc);
        Assert.Equal([critical.Id, high.Id, medium.Id, low.Id], desc);
    }

    [Fact]
    public async Task List_SortByCreatedAt_IsPlainDatabaseOrder()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var oldCritical = await CreateReportAsync(location, severity: ReportSeverity.Critical, createdAt: new DateTime(2021, 2, 1));
        var newLow = await CreateReportAsync(location, severity: ReportSeverity.Low, createdAt: new DateTime(2021, 2, 2));

        var asc = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}&sortBy=createdAt&sortOrder=asc", token)));
        var desc = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}&sortBy=createdAt", token)));

        Assert.Equal([oldCritical.Id, newLow.Id], asc);
        Assert.Equal([newLow.Id, oldCritical.Id], desc);
    }

    [Fact]
    public async Task List_PrioritySort_WithExactTie_FallsBackToUuidV7Order()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var sameSecond = new DateTime(2021, 3, 1, 12, 0, 0);
        var first = await CreateReportAsync(location, severity: ReportSeverity.High, createdAt: sameSecond);
        await Task.Delay(5);
        var second = await CreateReportAsync(location, severity: ReportSeverity.High, createdAt: sameSecond);

        var desc = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}", token)));
        var asc = Ids(await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}&sortOrder=asc", token)));

        Assert.Equal([second.Id, first.Id], desc);
        Assert.Equal([first.Id, second.Id], asc);
    }

    [Fact]
    public async Task List_Paginates_AndReportsMeta()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        for (var i = 0; i < 5; i++) await CreateReportAsync(location);

        var defaults = await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}", token));
        var paged = await DataAsync(await GetAsync($"/api/reports?locationId={location.Id}&limit=2&offset=4", token));

        Assert.Equal(20, defaults.GetProperty("meta").GetProperty("limit").GetInt32());
        Assert.Equal(5, defaults.GetProperty("meta").GetProperty("total").GetInt32());
        Assert.Equal(1, paged.GetProperty("items").GetArrayLength());
        Assert.Equal(4, paged.GetProperty("meta").GetProperty("offset").GetInt32());
    }

    [Theory]
    [InlineData("sortBy=password")]
    [InlineData("limit=201")]
    [InlineData("limit=0")]
    [InlineData("offset=-1")]
    [InlineData("month=2020-13")]
    [InlineData("sortOrder=sideways")]
    [InlineData("category=nope")]
    public async Task List_WithInvalidQuery_Returns422(string query)
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await GetAsync($"/api/reports?{query}", token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- Hauptmeldung ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ParentReport_LinksChildAndParent()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var parent = await CreateReportAsync(location);
        var child = await CreateReportAsync(location);

        var data = await DataAsync(await PatchAsync($"/api/reports/{child.Id}", new { parentReportId = parent.Id }, token));

        Assert.Equal(parent.Id, data.GetProperty("parentReportId").GetGuid());
        var parentData = await DataAsync(await GetAsync($"/api/reports/{parent.Id}", token));
        Assert.Equal([child.Id], parentData.GetProperty("childReportIds").EnumerateArray().Select(e => e.GetGuid()).ToList());
    }

    [Fact]
    public async Task Update_RejectsAReportAsItsOwnParent()
    {
        var (_, token) = await AdminWithTokenAsync();
        var report = await CreateReportAsync(await CreateLocationAsync());

        var response = await PatchAsync($"/api/reports/{report.Id}", new { parentReportId = report.Id }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ein Report kann nicht sein eigener Hauptreport sein.",
            (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Update_WithUnknownParent_Returns404()
    {
        var (_, token) = await AdminWithTokenAsync();
        var report = await CreateReportAsync(await CreateLocationAsync());

        var response = await PatchAsync($"/api/reports/{report.Id}", new { parentReportId = Guid.NewGuid() }, token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Hauptreport wurde nicht gefunden.", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Update_RejectsCyclesThroughGrandparents()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var grandparent = await CreateReportAsync(location);
        var parent = await CreateReportAsync(location);
        var child = await CreateReportAsync(location);
        await PatchAsync($"/api/reports/{parent.Id}", new { parentReportId = grandparent.Id }, token);
        await PatchAsync($"/api/reports/{child.Id}", new { parentReportId = parent.Id }, token);

        var response = await PatchAsync($"/api/reports/{grandparent.Id}", new { parentReportId = child.Id }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Zyklische Verknüpfung nicht erlaubt.", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task DeleteParent_RemovesTheLink()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var parent = await CreateReportAsync(location);
        var child = await CreateReportAsync(location);
        await PatchAsync($"/api/reports/{child.Id}", new { parentReportId = parent.Id }, token);

        var response = await DeleteAsync($"/api/reports/{child.Id}/parent", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await DataAsync(response)).GetProperty("parentReportId").ValueKind);
    }

    // --- Benachrichtigung ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_ReportsTheNewReportToTheNotificationService()
    {
        var a = await CreateLocationAsync();
        var b = await CreateLocationAsync();

        var response = await PostAsync("/api/reports", Body(a, "virus", "critical", description: "Verdacht auf Virus",
            extra: new() { ["locationIds"] = new[] { a.Id, b.Id } }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reportId = (await DataAsync(response)).GetProperty("id").GetGuid();
        var sent = Assert.Single(Factory.Events.Sent, e => e.ReportId == reportId);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), sent.LocationIds.Order());
    }

    [Fact]
    public async Task Create_ReportsNothing_WhenTheRequestIsInvalid()
    {
        var before = Factory.Events.Sent.Count;

        var response = await PostAsync("/api/reports", new { personnelNumber = 4711 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(before, Factory.Events.Sent.Count);
    }

    // --- Filialen von Rollen-Rechte ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_AcceptsALocationCreatedAfterTheListWasCached()
    {
        var (_, token) = await AdminWithTokenAsync();
        await GetAsync("/api/reports", token);
        var fresh = await CreateLocationAsync();

        var response = await PostAsync("/api/reports", Body(fresh));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_Returns503_WhenTheLocationsAreNotReachable()
    {
        var location = await CreateLocationAsync();
        Factory.Locations.Unavailable = true;

        var response = await PostAsync("/api/reports", Body(location));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ReportsShowTheLocationNamesFromTheLocationService()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync("Filiale Nord", "0042");
        var report = await CreateReportAsync(location);

        var data = await DataAsync(await GetAsync($"/api/reports/{report.Id}", token));

        var shown = data.GetProperty("locations")[0];
        Assert.Equal("Filiale Nord", shown.GetProperty("name").GetString());
        Assert.Equal("0042", shown.GetProperty("storeCode").GetString());
    }
}
