using Microsoft.EntityFrameworkCore;
using Workforce.Core.Employees;
using Workforce.Core.WorkingTime;

namespace Workforce.Infrastructure.WorkingTime;

/// <summary>The one row with the rules HR set for the working time account (no row = defaults).</summary>
public sealed class WorkingTimeRulesRecord
{
    public int Id { get; set; } = 1;
    public decimal PayoutThresholdHours { get; set; }
    public int? ExpiryMonths { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid ChangedBy { get; set; }
}

/// <summary>The monthly threshold (hours) of an employment relationship of the selection list.</summary>
public sealed class MonthlyThresholdRecord
{
    public Guid RelationshipId { get; set; }
    public decimal Hours { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid ChangedBy { get; set; }
}

public sealed class EfWorkingTimeStore(WorkforceDbContext db) : IWorkingTimeStore
{
    public async Task<IReadOnlyList<TimeBooking>> BookingsAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.TimeBookings.Where(b => b.EmployeeId == employeeId).ToListAsync(cancellationToken);

    public void AddBooking(TimeBooking booking) => db.TimeBookings.Add(booking);

    public async Task<IReadOnlyList<MonthClose>> ClosesAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.MonthCloses.AsNoTracking().Where(c => c.EmployeeId == employeeId).ToListAsync(cancellationToken);

    public async Task<MonthClose?> CloseAsync(Guid employeeId, int year, int month, CancellationToken cancellationToken) =>
        await db.MonthCloses.AsNoTracking().FirstOrDefaultAsync(c => c.EmployeeId == employeeId && c.Year == year && c.Month == month, cancellationToken);

    public void AddClose(MonthClose close) => db.MonthCloses.Add(close);

    public async Task<WorkingTimeRulesSetting?> GetRulesAsync(CancellationToken cancellationToken) =>
        await db.WorkingTimeRules.AsNoTracking().Where(r => r.Id == 1)
            .Select(r => new WorkingTimeRulesSetting(new WorkingTimeRules(r.PayoutThresholdHours, r.ExpiryMonths), r.ChangedAt, r.ChangedBy))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SetRulesAsync(WorkingTimeRulesSetting setting, CancellationToken cancellationToken)
    {
        var row = await db.WorkingTimeRules.FirstOrDefaultAsync(r => r.Id == 1, cancellationToken);
        if (row is null)
        {
            row = new WorkingTimeRulesRecord();
            db.WorkingTimeRules.Add(row);
        }

        row.PayoutThresholdHours = setting.Rules.PayoutThresholdHours;
        row.ExpiryMonths = setting.Rules.ExpiryMonths;
        row.ChangedAt = setting.ChangedAt;
        row.ChangedBy = setting.ChangedBy;
    }

    public async Task<IReadOnlyList<MonthlyThreshold>> ThresholdsAsync(CancellationToken cancellationToken) =>
        (await db.MonthlyThresholds.AsNoTracking().OrderBy(t => t.RelationshipId).ToListAsync(cancellationToken)).Select(t => new MonthlyThreshold(t.RelationshipId, t.Hours)).ToList();

    public async Task<decimal?> ThresholdAsync(Guid relationshipId, CancellationToken cancellationToken) =>
        await db.MonthlyThresholds.AsNoTracking().Where(t => t.RelationshipId == relationshipId).Select(t => (decimal?)t.Hours).FirstOrDefaultAsync(cancellationToken);

    public async Task SetThresholdAsync(Guid relationshipId, decimal? hours, Guid changedBy, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var row = await db.MonthlyThresholds.FirstOrDefaultAsync(t => t.RelationshipId == relationshipId, cancellationToken);
        if (hours is null)
        {
            if (row is not null)
            {
                db.MonthlyThresholds.Remove(row);
            }

            return;
        }

        if (row is null)
        {
            row = new MonthlyThresholdRecord { RelationshipId = relationshipId };
            db.MonthlyThresholds.Add(row);
        }

        row.Hours = hours.Value;
        row.ChangedAt = at;
        row.ChangedBy = changedBy;
    }
}

public sealed class EfEmploymentLookup(WorkforceDbContext db) : IEmploymentLookup
{
    public async Task<Guid?> RelationshipOfAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.EmployeeCards.AsNoTracking().Where(c => c.EmployeeId == employeeId).Select(c => c.EmploymentRelationshipId).FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> IsRelationshipAsync(Guid entryId, CancellationToken cancellationToken) =>
        await db.ReferenceEntries.AsNoTracking().AnyAsync(e => e.Id == entryId && e.Kind == ReferenceKind.EmploymentRelationship, cancellationToken);
}
