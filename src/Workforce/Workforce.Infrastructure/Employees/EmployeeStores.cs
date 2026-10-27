using Microsoft.EntityFrameworkCore;
using Workforce.Core.Employees;
using Workforce.Infrastructure.People;

namespace Workforce.Infrastructure.Employees;

/// <summary>The one row of the number series: the last personnel number given out (HR enters the first value; BC numbers above it raise it).</summary>
public sealed class PersonnelNumberSeriesRecord
{
    public int Id { get; set; } = 1;
    public long LastNumber { get; set; }
    public long StartNumber { get; set; } = 1001;
    public long EndNumber { get; set; } = 99999;
    public Guid ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

public sealed class EfEmployeeCardStore(WorkforceDbContext db) : IEmployeeCardStore
{
    public async Task<EmployeeCard?> GetAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.EmployeeCards.FirstOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);

    public void Add(EmployeeCard card)
    {
        db.EmployeeCards.Add(card);
        // The service's own copy of the person (as if BC had reported it), so that absences and the rest work at once.
        db.People.Add(new PersonRecord
        {
            EmployeeId = card.EmployeeId,
            PersonnelNumber = card.PersonnelNumber,
            Name = card.FullName,
            HomeStoreId = card.MainStoreId,
            BcHomeStoreId = card.MainStoreId,
            EntryDate = card.HireDate,
            ExitDate = card.ExitDate,
        });
    }

    public async Task CardChangedAsync(EmployeeCard card, CancellationToken cancellationToken)
    {
        if (await db.People.FindAsync([card.EmployeeId], cancellationToken) is not { } person)
        {
            return;
        }

        person.Name = card.FullName;
        person.BcHomeStoreId = card.MainStoreId;
        // The main store maintained in Access wins over what the card says (decision 21).
        person.HomeStoreId = (await db.MainStoreCopies.FindAsync([card.EmployeeId], cancellationToken))?.StoreId ?? card.MainStoreId;
        person.EntryDate = card.HireDate;
        person.ExitDate = card.ExitDate;
    }

    public async Task<bool> PersonnelNumberExistsAsync(string personnelNumber, CancellationToken cancellationToken) =>
        await db.EmployeeCards.AnyAsync(c => c.PersonnelNumber == personnelNumber, cancellationToken)
        || await db.People.AnyAsync(p => p.PersonnelNumber == personnelNumber, cancellationToken);

    public async Task<Guid?> FindByPersonnelNumberAsync(string personnelNumber, CancellationToken cancellationToken) =>
        await db.EmployeeCards.AsNoTracking().Where(c => c.PersonnelNumber == personnelNumber).Select(c => (Guid?)c.EmployeeId).FirstOrDefaultAsync(cancellationToken)
        ?? await db.People.AsNoTracking().Where(p => p.PersonnelNumber == personnelNumber).Select(p => (Guid?)p.EmployeeId).FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> StoreIsActiveAsync(Guid storeId, CancellationToken cancellationToken) =>
        await db.Stores.AsNoTracking().AnyAsync(s => s.StoreId == storeId && s.IsActive, cancellationToken);
}

public sealed class EfReferenceLists(WorkforceDbContext db) : IReferenceLists
{
    public async Task<IReadOnlyList<ReferenceEntry>> ListAsync(ReferenceKind kind, CancellationToken cancellationToken) =>
        await db.ReferenceEntries.Where(e => e.Kind == kind).OrderBy(e => e.Name).ToListAsync(cancellationToken);

    public async Task<ReferenceEntry?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ReferenceEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(ReferenceEntry entry) => db.ReferenceEntries.Add(entry);
}

public sealed class EfPersonnelNumberSeries(WorkforceDbContext db) : IPersonnelNumberSeries
{
    public async Task<NumberSeriesState?> GetAsync(CancellationToken cancellationToken) =>
        await db.PersonnelNumberSeries.AsNoTracking().Where(s => s.Id == 1)
            .Select(s => new NumberSeriesState(s.StartNumber, s.EndNumber, s.LastNumber)).FirstOrDefaultAsync(cancellationToken);

    public async Task SetAsync(NumberSeriesState state, Guid changedBy, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var row = await db.PersonnelNumberSeries.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (row is null)
        {
            row = new PersonnelNumberSeriesRecord();
            db.PersonnelNumberSeries.Add(row);
        }

        (row.StartNumber, row.EndNumber, row.LastNumber) = (state.Start, state.End, state.Last);
        row.ChangedBy = changedBy;
        row.ChangedAt = at;
    }

    /// <summary>
    /// One statement: the row is locked until the transaction ends, so any number of parallel creations queue up and each gets the next number;
    /// after the end of the range no row is returned. A rolled-back creation gives its number back.
    /// </summary>
    public async Task<long?> NextAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<long>($"UPDATE workforce.personnel_number_series SET last_number = last_number + 1 WHERE id = 1 AND last_number < end_number RETURNING last_number AS \"Value\"")
            .ToListAsync(cancellationToken)) is [var number] ? number : null;
}
