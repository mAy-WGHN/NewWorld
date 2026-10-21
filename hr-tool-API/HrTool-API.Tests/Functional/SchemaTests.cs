using HrTool_API.Authentication;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using HrTool_API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HrTool_API.Tests.Functional;

/// <summary>Das Schema (Migration gegen echtes Postgres): Eindeutigkeiten, Löschverhalten, Spaltenformate und der Scope-Filter nach Filiale.</summary>
public class SchemaTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string Code() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static Guid NewLocation() => Guid.CreateVersion7();

    private async Task<Workplace> AddWorkplaceAsync(Guid? locationId = null, string? code = null) =>
        await Factory.WithDbAsync(async (db, _) =>
        {
            var w = new Workplace { Code = code ?? Code(), LocationId = locationId ?? NewLocation() };
            db.Workplaces.Add(w);
            await db.SaveChangesAsync();
            return w;
        });

    private async Task<Employee> AddEmployeeAsync(Guid? locationId, string? no = null)
    {
        var workplaceId = locationId is { } l ? await WorkplaceForAsync(l) : (Guid?)null;
        return await Factory.WithDbAsync(async (db, _) =>
        {
            var e = new Employee { EmployeeNo = no ?? Digits(8), FirstName = "Vor", LastName = "Nach", WorkplaceId = workplaceId };
            db.Employees.Add(e);
            await db.SaveChangesAsync();
            return e;
        });
    }

    private static string Digits(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => Random.Shared.Next(1, 10)));

    private async Task<Schedule> AddScheduleAsync(Guid locationId, int year = 2026, int month = 10)
    {
        var workplaceId = await WorkplaceForAsync(locationId);
        return await Factory.WithDbAsync(async (db, _) =>
        {
            var s = new Schedule { Number = "DP" + Code() + "01", WorkplaceId = workplaceId, Year = year, Month = month };
            db.Schedules.Add(s);
            await db.SaveChangesAsync();
            return s;
        });
    }

    /// <summary>Führt die Abfrage so aus, wie die Rechteprüfung den Scope einer Anfrage setzt.</summary>
    private Task<T> ScopedAsync<T>(ScopeSet scope, Func<HrToolDbContext, Task<T>> query) =>
        Factory.WithDbAsync(async (db, sp) =>
        {
            sp.GetRequiredService<ScopeContext>().Current = scope;
            return await query(db);
        });

    // --- Schema ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheMigration_CreatesAllTables_InSnakeCase()
    {
        var tables = await Factory.WithDbAsync((db, _) =>
            db.Database.SqlQueryRaw<string>("select table_name as \"Value\" from information_schema.tables where table_schema = 'public'").ToListAsync());

        foreach (var expected in new[]
                 {
                     "workplaces", "planning_roles", "employment_contracts", "employees", "cashier_profiles",
                     "shift_patterns", "absence_types", "absences", "schedules", "shifts"
                 })
            Assert.Contains(expected, tables);
    }

    [Fact]
    public async Task EmployeeNo_KeepsLeadingZeros_AndIsUnique()
    {
        var no = "0" + Digits(4);
        var employee = await AddEmployeeAsync(null, no);

        var stored = await Factory.WithDbAsync((db, _) => db.Employees.SingleAsync(e => e.Id == employee.Id));
        Assert.Equal(no, stored.EmployeeNo);
        await Assert.ThrowsAsync<DbUpdateException>(() => AddEmployeeAsync(null, no));
    }

    [Fact]
    public async Task AWorkplaceCode_BelongsToExactlyOneLocation_AndViceVersa()
    {
        var code = Code();
        var location = NewLocation();
        await AddWorkplaceAsync(location, code);

        await Assert.ThrowsAsync<DbUpdateException>(() => AddWorkplaceAsync(NewLocation(), code)); // Kürzel doppelt
        await Assert.ThrowsAsync<DbUpdateException>(() => AddWorkplaceAsync(location, Code())); // zweites Kürzel für dieselbe Filiale
    }

    [Fact]
    public async Task APlanIsUniquePerLocationAndMonth()
    {
        var location = NewLocation();
        await AddScheduleAsync(location, 2026, 11);

        await Assert.ThrowsAsync<DbUpdateException>(() => AddScheduleAsync(location, 2026, 11));
        await AddScheduleAsync(location, 2026, 12); // anderer Monat geht
        await AddScheduleAsync(NewLocation(), 2026, 11); // andere Filiale geht
    }

    [Fact]
    public async Task Enums_AreStoredLowercase_AndDatesAndTimesRoundTrip()
    {
        var location = NewLocation();
        var employee = await AddEmployeeAsync(location);
        var schedule = await AddScheduleAsync(location);
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Schedules.Single(s => s.Id == schedule.Id).Status = ScheduleStatus.Confirmed;
            db.Shifts.Add(new Shift
            {
                ScheduleId = schedule.Id, EmployeeId = employee.Id, Date = new DateOnly(2026, 10, 4),
                StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(17, 15)
            });
            await db.SaveChangesAsync();
        });

        var (status, shift) = await Factory.WithDbAsync(async (db, _) => (
            (await db.Database.SqlQuery<string>($"select status as \"Value\" from schedules where id = {schedule.Id}").ToListAsync()).Single(),
            await db.Shifts.SingleAsync(s => s.ScheduleId == schedule.Id)));

        Assert.Equal("confirmed", status);
        Assert.Equal(new DateOnly(2026, 10, 4), shift.Date);
        Assert.Equal(new TimeOnly(17, 15), shift.EndTime);
    }

    [Fact]
    public async Task DeletingAPlan_DeletesItsShifts_ButAnEmployeeWithShiftsCannotBeDeleted()
    {
        var location = NewLocation();
        var employee = await AddEmployeeAsync(location);
        var schedule = await AddScheduleAsync(location);
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Shifts.Add(new Shift { ScheduleId = schedule.Id, EmployeeId = employee.Id, Date = new DateOnly(2026, 10, 5), ShiftCode = "VKA1" });
            await db.SaveChangesAsync();
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => Factory.WithDbAsync(async (db, _) =>
        {
            db.Employees.Remove(await db.Employees.SingleAsync(e => e.Id == employee.Id));
            await db.SaveChangesAsync();
        }));

        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Schedules.Remove(await db.Schedules.SingleAsync(s => s.Id == schedule.Id));
            await db.SaveChangesAsync();
        });
        Assert.False(await Factory.WithDbAsync((db, _) => db.Shifts.AnyAsync(s => s.ScheduleId == schedule.Id)));
    }

    [Fact]
    public async Task TheCashierProfile_IsOneToOne_AndDiesWithTheEmployee()
    {
        var employee = await AddEmployeeAsync(null);
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.CashierProfiles.Add(new CashierProfile { EmployeeId = employee.Id, PermissionGroup = "KASSIERER", StoreCode = "0010" });
            await db.SaveChangesAsync();
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => Factory.WithDbAsync(async (db, _) =>
        {
            db.CashierProfiles.Add(new CashierProfile { EmployeeId = employee.Id });
            await db.SaveChangesAsync();
        }));

        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Employees.Remove(await db.Employees.SingleAsync(e => e.Id == employee.Id));
            await db.SaveChangesAsync();
        });

        Assert.False(await Factory.WithDbAsync((db, _) => db.CashierProfiles.AnyAsync(c => c.EmployeeId == employee.Id)));
    }

    [Fact]
    public async Task UpdatedAt_IsTouchedOnChange()
    {
        var employee = await AddEmployeeAsync(null);
        var before = employee.UpdatedAt;
        await Task.Delay(1100); // Spalte hat Sekundengenauigkeit

        var after = await Factory.WithDbAsync(async (db, _) =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == employee.Id);
            e.Function = "Aushilfe";
            await db.SaveChangesAsync();
            return e.UpdatedAt;
        });

        Assert.True(after > before);
    }

    [Fact]
    public async Task AVacationRequest_KeepsItsApprovalData_AndOtherAbsencesNeedNone()
    {
        var employee = await AddEmployeeAsync(null);
        var (vacation, sick) = await Factory.WithDbAsync(async (db, _) =>
        {
            var v = new AbsenceType { Code = Code(), Description = "Urlaub", RequiresApproval = true };
            var s = new AbsenceType { Code = Code(), Description = "Krank" };
            db.AbsenceTypes.AddRange(v, s);
            await db.SaveChangesAsync();
            return (v, s);
        });
        var requestedAt = new DateTime(2026, 10, 8, 11, 55, 30, DateTimeKind.Utc);
        var (request, other) = await Factory.WithDbAsync(async (db, _) =>
        {
            var r = new Absence
            {
                EmployeeId = employee.Id, AbsenceTypeId = vacation.Id, DateFrom = new DateOnly(2026, 12, 24), DateTo = new DateOnly(2026, 12, 29),
                Days = 2, VacationDays = 2, ApprovalStatus = "Vorgesetzter akzeptiert", RequestedAt = requestedAt, Remark = "Herbstferien"
            };
            var o = new Absence { EmployeeId = employee.Id, AbsenceTypeId = sick.Id, DateFrom = new DateOnly(2026, 10, 1), DateTo = new DateOnly(2026, 10, 2), Days = 2 };
            db.Absences.AddRange(r, o);
            await db.SaveChangesAsync();
            return (r, o);
        });

        await Factory.WithDbAsync(async (db, _) =>
        {
            var r = await db.Absences.SingleAsync(a => a.Id == request.Id);
            r.ApprovalStatus = "Abgelehnt";
            r.RejectionReason = "Zeitraum bereits ausgebucht";
            await db.SaveChangesAsync();
        });

        var (storedRequest, storedOther, types) = await Factory.WithDbAsync(async (db, _) =>
            (await db.Absences.SingleAsync(a => a.Id == request.Id), await db.Absences.SingleAsync(a => a.Id == other.Id),
             await db.AbsenceTypes.Where(t => t.Id == vacation.Id || t.Id == sick.Id).ToDictionaryAsync(t => t.Id, t => t.RequiresApproval)));
        Assert.Equal("Abgelehnt", storedRequest.ApprovalStatus);
        Assert.Equal("Zeitraum bereits ausgebucht", storedRequest.RejectionReason);
        Assert.Equal(requestedAt, storedRequest.RequestedAt);
        Assert.Null(storedOther.ApprovalStatus);   // nicht genehmigungspflichtig: bleibt leer
        Assert.True(types[vacation.Id]);
        Assert.False(types[sick.Id]);
    }

    // --- Scope ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheScope_LimitsPlansEmployeesShiftsAndAbsences_ToTheirLocations()
    {
        var (locA, locB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var empA = await AddEmployeeAsync(locA);
        var empB = await AddEmployeeAsync(locB);
        var empWithoutLocation = await AddEmployeeAsync(null);
        var (planA, planB) = (await AddScheduleAsync(locA), await AddScheduleAsync(locB));
        var type = await Factory.WithDbAsync(async (db, _) =>
        {
            var t = new AbsenceType { Code = Code(), Description = "Urlaub" };
            db.AbsenceTypes.Add(t);
            db.Shifts.Add(new Shift { ScheduleId = planA.Id, EmployeeId = empA.Id, Date = new DateOnly(2026, 10, 1) });
            db.Shifts.Add(new Shift { ScheduleId = planB.Id, EmployeeId = empB.Id, Date = new DateOnly(2026, 10, 1) });
            db.Absences.Add(new Absence { EmployeeId = empA.Id, AbsenceTypeId = t.Id, DateFrom = new DateOnly(2026, 10, 2), DateTo = new DateOnly(2026, 10, 3), Days = 2 });
            db.Absences.Add(new Absence { EmployeeId = empB.Id, AbsenceTypeId = t.Id, DateFrom = new DateOnly(2026, 10, 2), DateTo = new DateOnly(2026, 10, 3), Days = 2 });
            await db.SaveChangesAsync();
            return t;
        });
        var scopeA = ScopeSet.Of([locA]);
        var mine = new[] { empA.Id, empB.Id, empWithoutLocation.Id };

        var employees = await ScopedAsync(scopeA, db => db.Employees.Where(e => mine.Contains(e.Id)).Select(e => e.Id).ToListAsync());
        var plans = await ScopedAsync(scopeA, db => db.Schedules.Where(s => s.Id == planA.Id || s.Id == planB.Id).Select(s => s.Id).ToListAsync());
        var shifts = await ScopedAsync(scopeA, db => db.Shifts.Where(s => s.ScheduleId == planA.Id || s.ScheduleId == planB.Id).Select(s => s.EmployeeId).ToListAsync());
        var absences = await ScopedAsync(scopeA, db => db.Absences.Where(a => a.AbsenceTypeId == type.Id).Select(a => a.EmployeeId).ToListAsync());

        Assert.Equal([empA.Id], employees);
        Assert.Equal([planA.Id], plans);
        Assert.Equal([empA.Id], shifts);
        Assert.Equal([empA.Id], absences);
    }

    [Fact]
    public async Task TheScope_Everything_AndNoScope_SeeAll_AnEmptyScopeSeesNothing()
    {
        var (inScope, withoutLocation) = (await AddEmployeeAsync(NewLocation()), await AddEmployeeAsync(null));
        var ids = new[] { inScope.Id, withoutLocation.Id };

        var everything = await ScopedAsync(ScopeSet.Everything, db => db.Employees.CountAsync(e => ids.Contains(e.Id)));
        var unscoped = await Factory.WithDbAsync((db, _) => db.Employees.CountAsync(e => ids.Contains(e.Id)));
        var empty = await ScopedAsync(ScopeSet.Of([]), db => db.Employees.CountAsync(e => ids.Contains(e.Id)));

        Assert.Equal(2, everything);
        Assert.Equal(2, unscoped);
        Assert.Equal(0, empty); // fail-closed: das Recht besteht, reicht aber nirgends hin
    }
}
