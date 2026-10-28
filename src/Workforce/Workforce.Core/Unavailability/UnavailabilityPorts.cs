using Workforce.Core.Absences;

namespace Workforce.Core.Unavailability;

/// <summary>Kinds and entries. Changes are saved by the caller (one unit of work with the outbox).</summary>
public interface IUnavailabilityStore
{
    Task<IReadOnlyList<UnavailabilityKind>> KindsAsync(CancellationToken cancellationToken);

    Task<UnavailabilityKind?> KindAsync(Guid id, CancellationToken cancellationToken);

    Task<UnavailabilityKind?> KindByCodeAsync(string code, CancellationToken cancellationToken);

    void AddKind(UnavailabilityKind kind);

    Task<IReadOnlyList<UnavailabilityEntry>> EntriesAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<UnavailabilityEntry?> EntryAsync(Guid id, CancellationToken cancellationToken);

    void AddEntry(UnavailabilityEntry entry);

    void RemoveEntry(UnavailabilityEntry entry);
}

/// <summary>A planned shift of a person (from the shift planning, which is not part of this project yet).</summary>
/// <param name="ShiftId">Id of the shift in the planning.</param>
/// <param name="StoreId">Store of the shift.</param>
/// <param name="From">Start of the shift (with offset).</param>
/// <param name="To">End of the shift.</param>
/// <param name="StoreLabel">Store number and name as shown to HR, e.g. "0010 BaBu Oberhausen".</param>
public sealed record PlannedShift(Guid ShiftId, Guid StoreId, string StoreLabel, DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// The shifts planned for a person. There is no shift planning yet; the default implementation returns nothing, so nothing is
/// reported until the planning exists. TODO(Entscheidung): the real source once shift planning is built.
/// </summary>
public interface IShiftLookup
{
    Task<IReadOnlyList<PlannedShift>> PlannedShiftsAsync(Guid employeeId, DateRange range, CancellationToken cancellationToken);
}
