using Microsoft.EntityFrameworkCore;
using Workforce.Core.Absences;
using Workforce.Core.Unavailability;

namespace Workforce.Infrastructure.Unavailability;

public sealed class EfUnavailabilityStore(WorkforceDbContext db) : IUnavailabilityStore
{
    public async Task<IReadOnlyList<UnavailabilityKind>> KindsAsync(CancellationToken cancellationToken) =>
        await db.UnavailabilityKinds.ToListAsync(cancellationToken);

    public async Task<UnavailabilityKind?> KindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.UnavailabilityKinds.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    /// <summary>The list is short (a few dozen): compared in memory, case-insensitive.</summary>
    public async Task<UnavailabilityKind?> KindByCodeAsync(string code, CancellationToken cancellationToken) =>
        (await db.UnavailabilityKinds.ToListAsync(cancellationToken)).FirstOrDefault(k => string.Equals(k.Code, code, StringComparison.OrdinalIgnoreCase));

    public void AddKind(UnavailabilityKind kind) => db.UnavailabilityKinds.Add(kind);

    public async Task<IReadOnlyList<UnavailabilityEntry>> EntriesAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.UnavailabilityEntries.Where(e => e.EmployeeId == employeeId).ToListAsync(cancellationToken);

    public async Task<UnavailabilityEntry?> EntryAsync(Guid id, CancellationToken cancellationToken) =>
        await db.UnavailabilityEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void AddEntry(UnavailabilityEntry entry) => db.UnavailabilityEntries.Add(entry);

    public void RemoveEntry(UnavailabilityEntry entry) => db.UnavailabilityEntries.Remove(entry);
}

/// <summary>There is no shift planning yet: no shifts, so nothing is reported as uncovered. TODO(Entscheidung): replaced when the planning exists.</summary>
public sealed class NoShiftPlanning : IShiftLookup
{
    public Task<IReadOnlyList<PlannedShift>> PlannedShiftsAsync(Guid employeeId, DateRange range, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlannedShift>>([]);
}
