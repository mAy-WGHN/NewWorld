using Workforce.Core.Absences;
using Workforce.Core.Unavailability;

namespace Workforce.Core.Tests;

internal sealed class FakeUnavailabilityStore : IUnavailabilityStore
{
    public FakeUnavailabilityStore()
    {
        // Same list as the migration seeds.
        foreach (var seed in UnavailabilityKindSeed.All)
        {
            Kinds.Add(seed.ToKind());
        }
    }

    public List<UnavailabilityKind> Kinds { get; } = [];

    public List<UnavailabilityEntry> Entries { get; } = [];

    public Task<IReadOnlyList<UnavailabilityKind>> KindsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UnavailabilityKind>>([.. Kinds]);

    public Task<UnavailabilityKind?> KindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Kinds.FirstOrDefault(k => k.Id == id));

    public Task<UnavailabilityKind?> KindByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Kinds.FirstOrDefault(k => string.Equals(k.Code, code, StringComparison.OrdinalIgnoreCase)));

    public void AddKind(UnavailabilityKind kind) => Kinds.Add(kind);

    public Task<IReadOnlyList<UnavailabilityEntry>> EntriesAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnavailabilityEntry>>([.. Entries.Where(e => e.EmployeeId == employeeId)]);

    public Task<UnavailabilityEntry?> EntryAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

    public void AddEntry(UnavailabilityEntry entry) => Entries.Add(entry);

    public void RemoveEntry(UnavailabilityEntry entry) => Entries.Remove(entry);
}

internal sealed class FakeShifts : IShiftLookup
{
    public List<(Guid EmployeeId, PlannedShift Shift)> Planned { get; } = [];

    public Task<IReadOnlyList<PlannedShift>> PlannedShiftsAsync(Guid employeeId, DateRange range, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlannedShift>>([.. Planned.Where(p => p.EmployeeId == employeeId && range.Contains(DateOnly.FromDateTime(p.Shift.From.DateTime))).Select(p => p.Shift)]);
}
