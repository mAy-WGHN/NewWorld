namespace Workforce.Core.Employees;

/// <summary>Employee cards. Changes are saved by the caller (one unit of work).</summary>
public interface IEmployeeCardStore
{
    Task<EmployeeCard?> GetAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>Adds the card and the service's own copy of the person (name, store, dates), so that absences work at once.</summary>
    void Add(EmployeeCard card);

    /// <summary>Keeps the copy of the person in step with a changed card.</summary>
    Task CardChangedAsync(EmployeeCard card, CancellationToken cancellationToken);

    /// <summary>Whether a person (card or copy from BC) has this personnel number.</summary>
    Task<bool> PersonnelNumberExistsAsync(string personnelNumber, CancellationToken cancellationToken);

    Task<Guid?> FindByPersonnelNumberAsync(string personnelNumber, CancellationToken cancellationToken);

    /// <summary>Whether the store is known (copy from BC) and active.</summary>
    Task<bool> StoreIsActiveAsync(Guid storeId, CancellationToken cancellationToken);
}

/// <summary>The selection lists of the card.</summary>
public interface IReferenceLists
{
    Task<IReadOnlyList<ReferenceEntry>> ListAsync(ReferenceKind kind, CancellationToken cancellationToken);

    Task<ReferenceEntry?> GetAsync(Guid id, CancellationToken cancellationToken);

    void Add(ReferenceEntry entry);
}

/// <summary>The number series of the personnel numbers: the range and the last number given out.</summary>
/// <param name="Start">First number of the range.</param>
/// <param name="End">Last number of the range.</param>
/// <param name="Last">Last number given out (the next person gets one more); one below <paramref name="Start"/> if none was given out yet.</param>
public sealed record NumberSeriesState(long Start, long End, long Last)
{
    /// <summary>How many numbers are left in the range.</summary>
    public long Remaining => Math.Max(0, End - Last);
}

/// <summary>The number series of the personnel numbers.</summary>
public interface IPersonnelNumberSeries
{
    /// <summary>The series, or <c>null</c> if HR has not entered the last used number yet.</summary>
    Task<NumberSeriesState?> GetAsync(CancellationToken cancellationToken);

    /// <summary>HR sets range and last given-out number.</summary>
    Task SetAsync(NumberSeriesState state, Guid changedBy, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the next number, <c>null</c> if the range is used up. Atomic: any number of parallel creations get distinct, consecutive numbers
    /// (they queue up on one row); if the unit of work fails the number is not used up.
    /// </summary>
    Task<long?> NextAsync(DateTimeOffset at, CancellationToken cancellationToken);
}
