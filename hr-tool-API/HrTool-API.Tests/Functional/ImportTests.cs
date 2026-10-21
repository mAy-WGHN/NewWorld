using HrTool_API.Commands;
using HrTool_API.Domain.Entities;
using HrTool_API.Services.Import;
using HrTool_API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Tests.Functional;

/// <summary>
/// Der Import der BC-Exporte (.xlsx): Zeilen werden angelegt, der zweite Lauf ist wiederholbar und überschreibt nicht, was HR pflegt, der Probelauf speichert nichts,
/// und die Meldungen enthalten nie Namen. Die Testdateien sind erfunden und haben die Spalten der echten Exporte.
/// </summary>
public class ImportTests(ApiFactory factory) : ApiTestBase(factory), IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hr-import-func", Guid.NewGuid().ToString("N"));
    private static int _year = 2040;

    private string No() => "T" + Random.Shared.Next(1000, 9999);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed record Data(string[] EmployeeNos, string PatternCode, string PlanNumber, int Year, string OddContract);

    /// <summary>Schreibt je Export eine kleine Datei in einen eigenen Ordner.</summary>
    private Data WriteExports(bool employees = true, bool cashiers = true, bool patterns = true, bool schedules = true)
    {
        Directory.CreateDirectory(_dir);
        string[] nos = [No(), No() + "b", No() + "c", No() + "d"];
        var patternCode = "ZZ/" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var plan = "DPT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var oddContract = "OHNE" + string.Concat(Guid.NewGuid().ToString("N")[..6].Select(c => char.IsDigit(c) ? (char)('G' + (c - '0')) : char.ToUpperInvariant(c)));   // ein Code ohne Zahl, wie VERKAUF im echten Export
        var year = Interlocked.Increment(ref _year);

        if (employees)
            XlsxBuilder.Write(Path.Combine(_dir, SpreadsheetImport.EmployeesFile),
                ["Nr.", "Vollständiger Name", "Vorname", "Nachname", "Funktion", "Geschäfts Email", "Suchbegriff", "Bemerkung", "Beschäftigungsverhältniscode", "Arbeitsort", "Rolle", "Region"],
                [
                    [nos[0], "Anna Importfrau", "Anna", "Importfrau", "Aushilfe", "anna@example.org", "ANNA IMPORTFRAU", "0", "TZ 15,25H", "AH", "VK", "REGION_4"],
                    [nos[1], "Nur Vollname", "", "", "", "", "NUR VOLLNAME", "0", oddContract, "XX", "", ""],          // nur der volle Name; Arbeitsort unbekannt; Vertrag ohne Stunden
                    [nos[2], "", "", "", "", "", "", "0", "", "", "", ""],                                          // ohne Namen: übersprungen
                    ["", "Ohne Nummer", "Ohne", "Nummer", "", "", "", "0", "", "", "", ""]                          // ohne Nummer: übersprungen
                ]);

        if (cashiers)
            XlsxBuilder.Write(Path.Combine(_dir, SpreadsheetImport.CashiersFile),
                ["ID", "Nachname", "Vorname", "Name auf Bon", "Arbeitsverhältnis", "Filialnr.", "Kasse Schnittstellenprofil", "Kasse Menüprofil", "POS Stilprofil", "Manager Benutzerrechte", "Berechtigungsgruppe"],
                [
                    [nos[0], "Importfrau", "Anna", "Importfrau", "Kassierer", "0012", "", "", "", "Nein", "KASSIERER"],
                    ["99999X", "Niemand", "Kein", "Niemand", "Verkäufer", "0010", "", "", "", "", "MANAGER"]       // gehört zu keinem Mitarbeiter
                ], shared: true);

        if (patterns)
            XlsxBuilder.Write(Path.Combine(_dir, SpreadsheetImport.ShiftPatternsFile),
                ["Code", "Beschreibung", "Musterart", "Anzahl Wochen"],
                [[patternCode, "Testmuster", "Wochenmuster", "4"], [patternCode + "X", "", "Unbekannt", "1"]], shared: true);

        if (schedules)
            XlsxBuilder.Write(Path.Combine(_dir, SpreadsheetImport.SchedulesFile),
                ["Nr.", "Arbeitsort", "Name", "Status", "Warnungen", "Total Tatsächlich (Stunden)", "Total Geplant (Std.)", "Total Budget  (Std.)", "Total Geplant (Betrag)", "Total Tatsächlich (Betrag)", "Zuletzt geändert"],
                [
                    [plan, "AH", $"März  {year}", "Bestätigt", "0", "0", "0", "1023.5", "0", "0", "44186"],
                    [plan + "2", "AH", $"April {year}", "Quartal", "0", "0", "0", "0", "0", "0", ""],            // Status unbekannt: übersprungen
                    [plan + "3", "ZZZ", $"Mai {year}", "Entwurf", "0", "0", "0", "0", "0", "0", ""]            // Arbeitsort unbekannt: übersprungen
                ], shared: true);

        return new Data(nos, patternCode, plan, year, oddContract);
    }

    private Task<ImportReport> RunAsync(bool dryRun = false) =>
        Factory.WithDbAsync((db, _) => new SpreadsheetImport(db).RunAsync(_dir, dryRun, default));

    // --- Anlegen ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Import_CreatesEmployeesWithTheirMasterData_ProfilesPatternsAndPlans()
    {
        var data = WriteExports();

        var report = await RunAsync();

        var (anna, nameOnly, skipped) = await Factory.WithDbAsync(async (db, _) =>
        (
            await db.Employees.Include(e => e.Workplace).Include(e => e.PlanningRole).Include(e => e.Contract).Include(e => e.CashierProfile).SingleAsync(e => e.EmployeeNo == data.EmployeeNos[0]),
            await db.Employees.Include(e => e.Contract).SingleAsync(e => e.EmployeeNo == data.EmployeeNos[1]),
            await db.Employees.CountAsync(e => e.EmployeeNo == data.EmployeeNos[2])
        ));
        Assert.Equal(("Anna", "Importfrau", "Aushilfe", "anna@example.org"), (anna.FirstName, anna.LastName, anna.Function, anna.Email));
        Assert.Equal("AH", anna.Workplace!.Code);
        Assert.Equal("VK", anna.PlanningRole!.Code);
        Assert.Equal(("TZ 15,25H", 15.25m), (anna.Contract!.Code, anna.Contract.WeeklyHours));
        Assert.Equal("REGION_4", anna.RegionCode);
        Assert.Equal(("Importfrau", "Kassierer", "0012", "KASSIERER", false), (anna.CashierProfile!.ReceiptName, anna.CashierProfile.WorkRelation, anna.CashierProfile.StoreCode, anna.CashierProfile.PermissionGroup, anna.CashierProfile.ManagerRights));
        Assert.Equal(("", "Nur Vollname"), (nameOnly.FirstName, nameOnly.LastName));   // nur der volle Name: er wird zum Nachnamen
        Assert.Null(nameOnly.WorkplaceId);                                              // Arbeitsort „XX“ ist nicht im Stamm
        Assert.Null(nameOnly.Contract!.WeeklyHours);                                    // ein Code ohne Zahl: keine Stunden
        Assert.Equal(0, skipped);

        var (pattern, plan, planCount) = await Factory.WithDbAsync(async (db, _) =>
        (
            await db.ShiftPatterns.SingleAsync(p => p.Code == data.PatternCode),
            await db.Schedules.Include(s => s.Workplace).SingleAsync(s => s.Number == data.PlanNumber),
            await db.Schedules.CountAsync(s => s.Number.StartsWith(data.PlanNumber))
        ));
        Assert.Equal(("Testmuster", ShiftPatternKind.Week, 4), (pattern.Description, pattern.Kind, pattern.WeekCount));
        Assert.Equal(("AH", data.Year, 3, ScheduleStatus.Confirmed, 1023.5m), (plan.Workplace!.Code, plan.Year, plan.Month, plan.Status, plan.BudgetHours));
        Assert.Equal(1, planCount);                                                      // die zwei nicht lesbaren Pläne fehlen

        Assert.Equal(2, report.Sections["Mitarbeiter"].Created);
        Assert.Equal(2, report.Sections["Mitarbeiter"].Skipped);
        Assert.Equal(1, report.Sections["Kassenprofile"].Created);
        Assert.Equal(1, report.Sections["Kassenprofile"].Skipped);
        Assert.Equal(1, report.Sections["Schichtmuster"].Created);
        Assert.Equal(1, report.Sections["Schichtmuster"].Skipped);
        Assert.Equal(1, report.Sections["Dienstpläne"].Created);
        Assert.Equal(2, report.Sections["Dienstpläne"].Skipped);
        Assert.Contains(report.Warnings, w => w.Contains("XX"));
        Assert.Contains(report.Warnings, w => w.Contains(data.OddContract));
    }

    [Fact]
    public async Task Import_MessagesContainCodesAndNumbersButNeverNames()
    {
        WriteExports();

        var report = await RunAsync();

        Assert.NotEmpty(report.Warnings);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("Importfrau") || w.Contains("Vollname") || w.Contains("Niemand") || w.Contains("example.org"));
    }

    // --- Wiederholen ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Import_IsRepeatable_UpdatesBcFields_ButKeepsWhatHrMaintains()
    {
        var data = WriteExports();
        await RunAsync();
        var otherWorkplace = await Factory.WithDbAsync(async (db, _) =>
        {
            // HR ändert Arbeitsort und Vertrag, bestätigt den Plan und passt das Budget an.
            var w = new Workplace { Code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), Name = "Andere" };
            db.Workplaces.Add(w);
            var contract = new EmploymentContract { Code = "HR-" + Guid.NewGuid().ToString("N")[..6], WeeklyHours = 40 };
            db.EmploymentContracts.Add(contract);
            var employee = await db.Employees.SingleAsync(e => e.EmployeeNo == data.EmployeeNos[0]);
            employee.WorkplaceId = w.Id;
            employee.ContractId = contract.Id;
            var plan = await db.Schedules.SingleAsync(s => s.Number == data.PlanNumber);
            plan.Status = ScheduleStatus.Draft;
            plan.BudgetHours = 5;
            await db.SaveChangesAsync();
            return w;
        });
        // BC ändert die Funktion und die E-Mail.
        XlsxBuilder.Write(Path.Combine(_dir, SpreadsheetImport.EmployeesFile),
            ["Nr.", "Vollständiger Name", "Vorname", "Nachname", "Funktion", "Geschäfts Email", "Suchbegriff", "Bemerkung", "Beschäftigungsverhältniscode", "Arbeitsort", "Rolle", "Region"],
            [[data.EmployeeNos[0], "Anna Importfrau", "Anna", "Importfrau", "Filialleitung", "neu@example.org", "", "0", "TZ 15,25H", "AB", "LG", "REGION_1"]]);

        var report = await RunAsync();

        var (anna, plan, counts) = await Factory.WithDbAsync(async (db, _) =>
        (
            await db.Employees.Include(e => e.Contract).SingleAsync(e => e.EmployeeNo == data.EmployeeNos[0]),
            await db.Schedules.SingleAsync(s => s.Number == data.PlanNumber),
            (Employees: await db.Employees.CountAsync(e => e.EmployeeNo.StartsWith("T")), Plans: await db.Schedules.CountAsync(s => s.Number == data.PlanNumber))
        ));
        Assert.Equal(("Filialleitung", "neu@example.org"), (anna.Function, anna.Email));   // aus BC: aktualisiert
        Assert.Equal(otherWorkplace.Id, anna.WorkplaceId);                                  // von HR gepflegt: bleibt
        Assert.Equal(40m, anna.Contract!.WeeklyHours);                                   // Vertrag von HR gepflegt: bleibt
        Assert.Equal("REGION_4", anna.RegionCode);
        Assert.Equal((ScheduleStatus.Draft, 5m), (plan.Status, plan.BudgetHours));          // Plan bleibt, wie HR ihn gepflegt hat
        Assert.Equal(1, counts.Plans);
        Assert.Equal(0, report.Sections["Mitarbeiter"].Created);
        Assert.Equal(1, report.Sections["Mitarbeiter"].Updated);
    }

    [Fact]
    public async Task Import_RunTwiceWithTheSameFiles_CreatesNothingTheSecondTime()
    {
        WriteExports();
        await RunAsync();

        var second = await RunAsync();

        Assert.All(second.Sections.Where(s => s.Key is "Mitarbeiter" or "Kassenprofile" or "Schichtmuster" or "Dienstpläne" or "Arbeitsorte" or "Nichtverfügbarkeitsarten"),
            s => Assert.Equal(0, s.Value.Created));
    }

    [Fact]
    public async Task DryRun_ReportsWhatWouldHappen_ButSavesNothing()
    {
        var data = WriteExports();

        var report = await RunAsync(dryRun: true);

        Assert.Equal(2, report.Sections["Mitarbeiter"].Created);
        Assert.Equal(0, await Factory.WithDbAsync((db, _) => db.Employees.CountAsync(e => e.EmployeeNo == data.EmployeeNos[0])));
        Assert.Equal(0, await Factory.WithDbAsync((db, _) => db.ShiftPatterns.CountAsync(p => p.Code == data.PatternCode)));
        Assert.Equal(0, await Factory.WithDbAsync((db, _) => db.Schedules.CountAsync(s => s.Number == data.PlanNumber)));
    }

    // --- Fehlende Dateien ------------------------------------------------------------------------------------------

    [Fact]
    public async Task MissingFiles_AreSkippedWithAWarning_AndAMissingFolderIsAnError()
    {
        WriteExports(employees: false, cashiers: false, schedules: false);

        var report = await RunAsync();

        Assert.Equal(3, report.Warnings.Count(w => w.Contains("fehlt im Ordner")));
        Assert.Equal(1, report.Sections["Schichtmuster"].Created);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            Factory.WithDbAsync((db, _) => new SpreadsheetImport(db).RunAsync(Path.Combine(_dir, "gibt-es-nicht"), false, default)));
    }

    // --- Befehl ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheCommand_PrintsASummary_ReturnsZero_AndSupportsTheDryRunAndADefaultErrorForAMissingFolder()
    {
        var data = WriteExports();

        var dry = new StringWriter();
        var dryCode = await ImportCommand.RunAsync(Factory.Services, [_dir, "--dry-run"], dry);
        var real = new StringWriter();
        var realCode = await ImportCommand.RunAsync(Factory.Services, [_dir], real);
        var missing = new StringWriter();
        var missingCode = await ImportCommand.RunAsync(Factory.Services, [Path.Combine(_dir, "nix")], missing);

        Assert.Equal(0, dryCode);
        Assert.Contains("Probelauf", dry.ToString());
        Assert.Equal(0, realCode);
        Assert.Contains("Import abgeschlossen", real.ToString());
        Assert.Contains("Mitarbeiter: 2 neu, 0 aktualisiert, 2 übersprungen", real.ToString());
        Assert.DoesNotContain("Importfrau", real.ToString());
        Assert.Equal(1, missingCode);
        Assert.Contains("existiert nicht", missing.ToString());
        Assert.Equal(1, await Factory.WithDbAsync((db, _) => db.Employees.CountAsync(e => e.EmployeeNo == data.EmployeeNos[0])));
    }
}
