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

/// <summary>The number series of the personnel numbers.</summary>
public interface IPersonnelNumberSeries
{
    /// <summary>The last number given out, or <c>null</c> if HR has not entered one yet.</summary>
    Task<long?> LastAsync(CancellationToken cancellationToken);

    /// <summary>HR sets the last given-out number (the next person gets one more).</summary>
    Task SetLastAsync(long last, Guid changedBy, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the next number. Atomic: two parallel creations never get the same number; if the unit of work fails the number is not used up.
    /// </summary>
    Task<long> NextAsync(DateTimeOffset at, CancellationToken cancellationToken);
}
