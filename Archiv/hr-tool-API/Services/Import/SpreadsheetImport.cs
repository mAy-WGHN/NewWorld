using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Services.Import;

/// <summary>Was der Import getan hat: je Bereich neu/aktualisiert/übersprungen und die Warnungen (nur Personalnummern und Codes, nie Namen).</summary>
public sealed class ImportReport
{
    public Dictionary<string, (int Created, int Updated, int Skipped)> Sections { get; } = new();
    public List<string> Warnings { get; } = [];

    public void Count(string section, int created = 0, int updated = 0, int skipped = 0)
    {
        var (c, u, s) = Sections.GetValueOrDefault(section);
        Sections[section] = (c + created, u + updated, s + skipped);
    }

    public void Warn(string message) => Warnings.Add(message);
}

/// <summary>
/// Liest die BC-Exporte (.xlsx) in die Tabellen, solange es keine BC-Anbindung gibt. Der Import ist <b>wiederholbar</b> (Schlüssel: Personalnummer, Code, Plannummer) und läuft in <b>einer Transaktion</b>.
/// <list type="bullet">
/// <item>Mitarbeiter: Name, Funktion und E-Mail kommen aus BC und werden bei jedem Import aktualisiert. Arbeitsort, Rolle, Vertrag und Region setzt der Import nur, solange sie leer sind —
///   danach pflegt sie HR (<c>PATCH /api/employees</c>) und der Import überschreibt sie nicht.</item>
/// <item>Pläne werden nur angelegt, nie verändert (Status und Budget pflegt HR). Schichten kommen nicht aus diesen Exporten.</item>
/// <item>Kassenprofile und Schichtmuster gehören BC und werden überschrieben.</item>
/// </list>
/// </summary>
public class SpreadsheetImport(HrToolDbContext db)
{
    public const string EmployeesFile = "Mitarbeiterliste (Personalplanung).xlsx";
    public const string CashiersFile = "Kassiererübersicht.xlsx";
    public const string ShiftPatternsFile = "Schichtmuster.xlsx";
    public const string SchedulesFile = "Mitarbeiterliste.xlsx";

    private const int MaxFieldWarnings = 200;

    public async Task<ImportReport> RunAsync(string folder, bool dryRun, CancellationToken ct)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Der Ordner {folder} existiert nicht.");

        // Mit Wiederholungsstrategie (EnableRetryOnFailure) ist eine eigene Transaktion nur innerhalb der Strategie erlaubt.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var report = new ImportReport();

            report.Count("Arbeitsorte", created: await ReferenceDataMaintenance.SeedWorkplacesAsync(db, ct));
            report.Count("Nichtverfügbarkeitsarten", created: await ReferenceDataMaintenance.SeedAbsenceTypesAsync(db, ct));

            var workplaces = (await db.Workplaces.ToListAsync(ct)).ToDictionary(w => w.Code, StringComparer.OrdinalIgnoreCase);
            var employees = await ImportEmployeesAsync(folder, workplaces, report, ct);
            await ImportCashiersAsync(folder, employees, report, ct);
            await ImportShiftPatternsAsync(folder, report, ct);
            await ImportSchedulesAsync(folder, workplaces, report, ct);

            await db.SaveChangesAsync(ct);
            if (dryRun) await transaction.RollbackAsync(ct);
            else await transaction.CommitAsync(ct);
            return report;
        });
    }

    private async Task<Dictionary<string, Employee>> ImportEmployeesAsync(string folder, Dictionary<string, Workplace> workplaces, ImportReport report, CancellationToken ct)
    {
        var employees = (await db.Employees.IgnoreQueryFilters().ToListAsync(ct)).ToDictionary(e => e.EmployeeNo, StringComparer.OrdinalIgnoreCase);
        var table = Read(folder, EmployeesFile, report);
        if (table is null) return employees;

        var roles = (await db.PlanningRoles.ToListAsync(ct)).ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        var contracts = (await db.EmploymentContracts.ToListAsync(ct)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var unknownWorkplaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var no = row["Nr."];
            var first = row["Vorname"];
            var last = row["Nachname"];
            if (first.Length == 0 && last.Length == 0) last = row["Vollständiger Name"];
            if (no.Length == 0 || last.Length == 0)
            {
                report.Count("Mitarbeiter", skipped: 1);
                Warn(report, $"Mitarbeiter ohne Nummer oder Namen übersprungen (Nr. „{no}“).");
                continue;
            }

            var isNew = !employees.TryGetValue(no, out var employee);
            if (isNew)
            {
                employee = new Employee { EmployeeNo = no };
                db.Employees.Add(employee);
                employees[no] = employee;
            }

            employee!.FirstName = first;
            employee.LastName = last;
            employee.Function = row.Text("Funktion");
            employee.Email = row.Text("Geschäfts Email");

            if (employee.WorkplaceId is null && row.Text("Arbeitsort") is { } code)
            {
                if (workplaces.TryGetValue(code, out var workplace)) employee.WorkplaceId = workplace.Id;
                else if (unknownWorkplaces.Add(code)) Warn(report, $"Arbeitsort „{code}“ ist nicht im Stamm (Mitarbeiter ohne Arbeitsort importiert).");
            }

            if (employee.PlanningRoleId is null && row.Text("Rolle") is { } roleCode)
            {
                if (!roles.TryGetValue(roleCode, out var role))
                {
                    role = new PlanningRole { Code = roleCode };
                    db.PlanningRoles.Add(role);
                    roles[roleCode] = role;
                    report.Count("Rollen", created: 1);
                }

                employee.PlanningRoleId = role.Id;
            }

            if (employee.ContractId is null && row.Text("Beschäftigungsverhältniscode") is { } contractCode)
            {
                if (!contracts.TryGetValue(contractCode, out var contract))
                {
                    contract = new EmploymentContract { Code = contractCode, WeeklyHours = ImportParsers.ParseWeeklyHours(contractCode) };
                    db.EmploymentContracts.Add(contract);
                    contracts[contractCode] = contract;
                    report.Count("Beschäftigungsverhältnisse", created: 1);
                    if (contract.WeeklyHours is null) Warn(report, $"Beschäftigungsverhältnis „{contractCode}“: keine Wochenstunden im Code (leer gelassen).");
                }

                employee.ContractId = contract.Id;
            }

            if (employee.RegionCode is null) employee.RegionCode = row.Text("Region");
            report.Count("Mitarbeiter", created: isNew ? 1 : 0, updated: isNew ? 0 : 1);
        }

        return employees;
    }

    private async Task ImportCashiersAsync(string folder, Dictionary<string, Employee> employees, ImportReport report, CancellationToken ct)
    {
        var table = Read(folder, CashiersFile, report);
        if (table is null) return;

        var profiles = (await db.CashierProfiles.IgnoreQueryFilters().ToListAsync(ct)).ToDictionary(p => p.EmployeeId);
        foreach (var row in table.Rows)
        {
            var id = row["ID"];
            if (!employees.TryGetValue(id, out var employee))
            {
                report.Count("Kassenprofile", skipped: 1);
                Warn(report, $"Kassierer-ID „{id}“ gehört zu keinem Mitarbeiter (übersprungen).");
                continue;
            }

            var isNew = !profiles.TryGetValue(employee.Id, out var profile);
            if (isNew)
            {
                profile = new CashierProfile { EmployeeId = employee.Id };
                db.CashierProfiles.Add(profile);
                profiles[employee.Id] = profile;
            }

            profile!.ReceiptName = row.Text("Name auf Bon");
            profile.WorkRelation = row.Text("Arbeitsverhältnis");
            profile.StoreCode = row.Text("Filialnr.");
            profile.PermissionGroup = row.Text("Berechtigungsgruppe");
            profile.ManagerRights = string.Equals(row["Manager Benutzerrechte"], "Ja", StringComparison.OrdinalIgnoreCase);
            profile.InterfaceProfile = row.Text("Kasse Schnittstellenprofil");
            profile.MenuProfile = row.Text("Kasse Menüprofil");
            profile.PosStyleProfile = row.Text("POS Stilprofil");
            report.Count("Kassenprofile", created: isNew ? 1 : 0, updated: isNew ? 0 : 1);
        }
    }

    private async Task ImportShiftPatternsAsync(string folder, ImportReport report, CancellationToken ct)
    {
        var table = Read(folder, ShiftPatternsFile, report);
        if (table is null) return;

        var patterns = (await db.ShiftPatterns.ToListAsync(ct)).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            var code = row["Code"];
            if (code.Length == 0 || ImportParsers.ParseShiftPatternKind(row["Musterart"]) is not { } kind)
            {
                report.Count("Schichtmuster", skipped: 1);
                Warn(report, $"Schichtmuster „{code}“: Code oder Musterart unbekannt (übersprungen).");
                continue;
            }

            var isNew = !patterns.TryGetValue(code, out var pattern);
            if (isNew)
            {
                pattern = new ShiftPattern { Code = code };
                db.ShiftPatterns.Add(pattern);
                patterns[code] = pattern;
            }

            pattern!.Description = row.Text("Beschreibung");
            pattern.Kind = kind;
            pattern.WeekCount = (int)ImportParsers.ParseNumber(row["Anzahl Wochen"]);
            report.Count("Schichtmuster", created: isNew ? 1 : 0, updated: isNew ? 0 : 1);
        }
    }

    private async Task ImportSchedulesAsync(string folder, Dictionary<string, Workplace> workplaces, ImportReport report, CancellationToken ct)
    {
        var table = Read(folder, SchedulesFile, report);
        if (table is null) return;

        var numbers = (await db.Schedules.IgnoreQueryFilters().Select(s => s.Number).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var periods = (await db.Schedules.IgnoreQueryFilters().Select(s => new { s.WorkplaceId, s.Year, s.Month }).ToListAsync(ct))
            .Select(s => (s.WorkplaceId, s.Year, s.Month)).ToHashSet();

        foreach (var row in table.Rows)
        {
            var number = row["Nr."];
            if (number.Length == 0) continue;
            if (numbers.Contains(number))
            {
                report.Count("Dienstpläne", skipped: 1);   // bestehende Pläne bleiben, wie HR sie gepflegt hat
                continue;
            }

            var code = row["Arbeitsort"];
            if (!workplaces.TryGetValue(code, out var workplace) || ImportParsers.ParseMonthName(row["Name"]) is not { } period
                || ImportParsers.ParseScheduleStatus(row["Status"]) is not { } status)
            {
                report.Count("Dienstpläne", skipped: 1);
                Warn(report, $"Plan {number}: Arbeitsort „{code}“, Zeitraum „{row["Name"]}“ oder Status „{row["Status"]}“ nicht lesbar (übersprungen).");
                continue;
            }

            if (!periods.Add((workplace.Id, period.Year, period.Month)))
            {
                report.Count("Dienstpläne", skipped: 1);
                Warn(report, $"Plan {number}: Für {code} gibt es {period.Month}/{period.Year} schon einen anderen Plan (übersprungen).");
                continue;
            }

            numbers.Add(number);
            db.Schedules.Add(new Schedule
            {
                Number = number, WorkplaceId = workplace.Id, Year = period.Year, Month = period.Month, Status = status,
                BudgetHours = ImportParsers.ParseNumber(row["Total Budget (Std.)"])
            });
            report.Count("Dienstpläne", created: 1);
        }
    }

    private static XlsxTable? Read(string folder, string file, ImportReport report)
    {
        var path = Path.Combine(folder, file);
        if (File.Exists(path)) return XlsxReader.ReadFirstSheet(path);
        Warn(report, $"{file} fehlt im Ordner (dieser Teil wird übersprungen).");
        return null;
    }

    private static void Warn(ImportReport report, string message)
    {
        if (report.Warnings.Count < MaxFieldWarnings) report.Warn(message);
    }
}
