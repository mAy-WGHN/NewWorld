using System.Net;
using HrTool_API.Domain.Entities;
using HrTool_API.Services;
using HrTool_API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Tests.Functional;

/// <summary>Nichtverfügbarkeitsarten (Stamm aus BC, 24) und Schichtmuster: Stamm, Lesen, Pflegen, Löschen.</summary>
public class MasterDataTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string Code(string prefix = "T") => (prefix + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    // --- Nichtverfügbarkeitsarten: Stamm -----------------------------------------------------------------------

    [Fact]
    public void TheAbsenceTypeSeed_HasThe24TypesOfTheExport_AndOnlyVacationNeedsApproval()
    {
        var seed = ReferenceDataMaintenance.ReadAbsenceTypeSeed();

        Assert.Equal(24, seed.Count);
        Assert.Equal(seed.Count, seed.Select(s => s.Code).Distinct().Count());
        Assert.Equal(["URLAUB"], seed.Where(s => s.RequiresApproval).Select(s => s.Code));
        Assert.Equal(["MUTTERSCHUTZ", "PRÜFUNG", "PRÜFUNGSVORBEREITUNG", "TOD", "UB"], seed.Where(s => s.PaidTime).Select(s => s.Code).Order());
        Assert.Equal("U", seed.Single(s => s.Code == "URLAUB").ScheduleSymbol);
        Assert.Equal("FT", seed.Single(s => s.Code == "FREI").ScheduleSymbol);            // im Export steht „FT “ mit Leerzeichen
        Assert.Null(seed.Single(s => s.Code == "KLASSENFAHRT").ScheduleSymbol);          // die einzige Art ohne Kürzel
        Assert.Equal("Freier Tag", seed.Single(s => s.Code == "FREI").Description);
    }

    [Fact]
    public async Task SeedingAbsenceTypes_CreatesTheMissingOnes_AndNeverOverwritesChanges()
    {
        await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.SeedAbsenceTypesAsync(db, default));
        await Factory.WithDbAsync(async (db, _) =>
        {
            var vacation = await db.AbsenceTypes.SingleAsync(t => t.Code == "URLAUB");
            vacation.Description = "Erholungsurlaub";
            await db.SaveChangesAsync();
        });

        var created = await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.SeedAbsenceTypesAsync(db, default));

        Assert.Equal(0, created);
        var vacationNow = await Factory.WithDbAsync((db, _) => db.AbsenceTypes.SingleAsync(t => t.Code == "URLAUB"));
        Assert.Equal("Erholungsurlaub", vacationNow.Description);
        Assert.True(vacationNow.RequiresApproval);
        Assert.True(await Factory.WithDbAsync((db, _) => db.AbsenceTypes.CountAsync()) >= 24);
    }

    // --- Nichtverfügbarkeitsarten: Routen ----------------------------------------------------------------------

    [Fact]
    public async Task AbsenceTypes_CanBeListedCreatedChangedAndDeleted()
    {
        var token = await AdminTokenAsync();
        await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.SeedAbsenceTypesAsync(db, default));
        var code = Code();

        var listed = (await DataAsync(await GetAsync("/api/absence-types", token))).EnumerateArray().ToList();
        var created = await PostAsync("/api/absence-types", new { code = " " + code.ToLowerInvariant() + " ", description = " Neue Art ", paidTime = true, scheduleSymbol = "NA", requiresApproval = false }, token);
        var id = (await DataAsync(created)).GetProperty("id").GetGuid();
        var patched = await PatchAsync($"/api/absence-types/{id}", new { description = "Geändert", requiresApproval = true, code = "ANDERS" }, token);
        var deleted = await DeleteAsync($"/api/absence-types/{id}", token);

        Assert.Contains(listed, t => t.GetProperty("code").GetString() == "URLAUB" && t.GetProperty("requiresApproval").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(code, (await DataAsync(created)).GetProperty("code").GetString());   // getrimmt, Großbuchstaben
        Assert.Equal("Neue Art", (await DataAsync(created)).GetProperty("description").GetString());
        var changed = await DataAsync(patched);
        Assert.Equal("Geändert", changed.GetProperty("description").GetString());
        Assert.True(changed.GetProperty("requiresApproval").GetBoolean());
        Assert.Equal(code, changed.GetProperty("code").GetString());                    // der Code ändert sich nie
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/absence-types/{id}", token)).StatusCode);
    }

    [Fact]
    public async Task AbsenceTypes_DuplicateCodes_AreRejected_AndUsedTypesCannotBeDeleted()
    {
        var token = await AdminTokenAsync();
        var type = await InsertAsync(new AbsenceType { Code = Code(), Description = "x" });
        var employee = await InsertAsync(new Employee { EmployeeNo = Unique("N"), FirstName = "V", LastName = "N" });
        await InsertAsync(new Absence { EmployeeId = employee.Id, AbsenceTypeId = type.Id, DateFrom = new DateOnly(2026, 1, 1), DateTo = new DateOnly(2026, 1, 1) });

        var duplicate = await PostAsync("/api/absence-types", new { code = type.Code.ToLowerInvariant(), description = "x" }, token);
        var invalid = await PostAsync("/api/absence-types", new { scheduleSymbol = "ZUVIELE-ZEICHEN" }, token);
        var inUse = await DeleteAsync($"/api/absence-types/{type.Id}", token);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync($"/api/absence-types/{Guid.NewGuid()}", token)).StatusCode);
    }

    // --- Schichtmuster ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ShiftPatterns_CanBeCreatedListedFilteredChangedAndDeleted()
    {
        var token = await AdminTokenAsync();
        var marker = Code("M");
        var day = await PostAsync("/api/shift-patterns", new { code = marker + "/A", description = "Frühschicht", kind = "day", weekCount = 4 }, token);
        var week = await PostAsync("/api/shift-patterns", new { code = marker + "/B", kind = "week", weekCount = 0 }, token);
        Assert.Equal(HttpStatusCode.Created, day.StatusCode);
        var dayId = (await DataAsync(day)).GetProperty("id").GetGuid();

        var all = (await DataAsync(await GetAsync($"/api/shift-patterns?search={marker.ToLowerInvariant()}", token))).GetProperty("items").EnumerateArray().ToList();
        var onlyWeek = (await DataAsync(await GetAsync($"/api/shift-patterns?search={marker}&kind=week", token))).GetProperty("items").EnumerateArray().ToList();
        var byDescription = (await DataAsync(await GetAsync("/api/shift-patterns?search=Frühschicht", token))).GetProperty("items").EnumerateArray().ToList();
        var patched = await PatchAsync($"/api/shift-patterns/{dayId}", new { description = "Spätschicht", weekCount = 2, code = "X" }, token);

        Assert.Equal([marker + "/A", marker + "/B"], all.Select(p => p.GetProperty("code").GetString()));
        Assert.Equal([marker + "/B"], onlyWeek.Select(p => p.GetProperty("code").GetString()));
        Assert.Contains(byDescription, p => p.GetProperty("code").GetString() == marker + "/A");
        Assert.Equal("day", (await DataAsync(day)).GetProperty("kind").GetString());
        var changed = await DataAsync(patched);
        Assert.Equal(("Spätschicht", 2, marker + "/A"), (changed.GetProperty("description").GetString(), changed.GetProperty("weekCount").GetInt32(), changed.GetProperty("code").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/shift-patterns/{dayId}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/shift-patterns/{dayId}", token)).StatusCode);
    }

    [Fact]
    public async Task ShiftPatterns_Validation_AndDuplicateCodes()
    {
        var token = await AdminTokenAsync();
        var code = Code("D");
        await PostAsync("/api/shift-patterns", new { code, kind = "day", weekCount = 1 }, token);

        var duplicate = await PostAsync("/api/shift-patterns", new { code = code.ToLowerInvariant(), kind = "day", weekCount = 1 }, token);
        var missing = await PostAsync("/api/shift-patterns", new { }, token);
        var tooLong = await PostAsync("/api/shift-patterns", new { code = Code("L"), kind = "week", weekCount = 53 }, token);
        var unknownKind = await PostAsync("/api/shift-patterns", new { code = Code("K"), kind = "monat", weekCount = 1 }, token);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.All(new[] { missing, tooLong, unknownKind }, r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
    }

    [Fact]
    public async Task TheMasterDataRoutes_NeedTheirPrivileges()
    {
        var (_, readOnly) = await UserWithPrivilegesAsync("absence_type:read", "shift_pattern:read");

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/absence-types", readOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/shift-patterns", readOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/api/absence-types", new { code = Code(), description = "x" }, readOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/api/shift-patterns", new { code = Code(), kind = "day", weekCount = 1 }, readOnly)).StatusCode);
    }
}
