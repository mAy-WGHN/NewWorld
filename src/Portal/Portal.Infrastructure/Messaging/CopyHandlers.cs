using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Portal.Infrastructure.Reports;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;
using Workforce.Contracts;

namespace Portal.Infrastructure.Messaging;

/// <summary>Read-only copy of a person (from BC Integration events). Organisation data only (class 1).</summary>
public sealed class PersonRecord
{
    public Guid EmployeeId { get; set; }
    public required string Name { get; set; }

    /// <summary>The main store that counts: the one maintained in Access, otherwise <see cref="BcHomeStoreId"/>.</summary>
    public Guid? HomeStoreId { get; set; }

    /// <summary>BC's suggestion for the main store (not always maintained there).</summary>
    public Guid? BcHomeStoreId { get; set; }

    public DateOnly? EntryDate { get; set; }
    public DateOnly? ExitDate { get; set; }
}

/// <summary>
/// The main store of a person as Access publishes it (the one maintained there, otherwise BC's suggestion), from <c>PermissionsChanged</c>.
/// Kept separately so it survives arriving before the person's copy; older versions are ignored.
/// </summary>
public sealed class MainStoreCopyRecord
{
    public Guid EmployeeId { get; set; }
    public Guid? StoreId { get; set; }
    public long Version { get; set; }
}

/// <summary>Read-only copy of a store.</summary>
public sealed class StoreRecord
{
    public Guid StoreId { get; set; }
    public required string StoreNumber { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
}

public sealed class EmployeeChangedHandler(PortalDbContext db) : IMessageHandler<EmployeeChanged>
{
    public async Task HandleAsync(EmployeeChanged message, CancellationToken cancellationToken)
    {
        var person = await db.People.FindAsync([message.EmployeeId], cancellationToken);
        if (person is null)
        {
            person = new PersonRecord { EmployeeId = message.EmployeeId, Name = message.Name };
            db.People.Add(person);
        }

        person.Name = message.Name;
        // BC only suggests the main store; if Access has told us one, that one counts.
        person.BcHomeStoreId = message.HomeStoreId;
        person.HomeStoreId = (await db.MainStoreCopies.FindAsync([message.EmployeeId], cancellationToken))?.StoreId ?? message.HomeStoreId;
        person.EntryDate = message.EntryDate;
        person.ExitDate = message.ExitDate;
    }
}

/// <summary>Records the entry date of a known person; the full state (name, store) follows in <c>EmployeeChanged</c>, which creates the copy.</summary>
public sealed class EmployeeHiredHandler(PortalDbContext db) : IMessageHandler<EmployeeHired>
{
    public async Task HandleAsync(EmployeeHired message, CancellationToken cancellationToken)
    {
        if (await db.People.FindAsync([message.EmployeeId], cancellationToken) is { } person)
        {
            person.EntryDate = message.EntryDate;
        }
    }
}

/// <summary>Records the exit date of a known person (the Portal has no absences of its own to end).</summary>
public sealed class EmployeeLeftHandler(PortalDbContext db) : IMessageHandler<EmployeeLeft>
{
    public async Task HandleAsync(EmployeeLeft message, CancellationToken cancellationToken)
    {
        if (await db.People.FindAsync([message.EmployeeId], cancellationToken) is { } person)
        {
            person.ExitDate = message.ExitDate;
        }
    }
}

public sealed class StoreChangedHandler(PortalDbContext db) : IMessageHandler<StoreChanged>
{
    public async Task HandleAsync(StoreChanged message, CancellationToken cancellationToken)
    {
        var store = await db.Stores.FindAsync([message.StoreId], cancellationToken);
        if (store is null)
        {
            store = new StoreRecord { StoreId = message.StoreId, StoreNumber = message.StoreNumber, Name = message.Name };
            db.Stores.Add(store);
        }

        store.StoreNumber = message.StoreNumber;
        store.Name = message.Name;
        store.IsActive = message.IsActive;
    }
}

/// <summary>Deletes the copy of the person and their permission copy.</summary>
public sealed class EmployeeDataErasureRequestedHandler(PortalDbContext db, PermissionsChangedHandler permissions) : IMessageHandler<EmployeeDataErasureRequested>
{
    public async Task HandleAsync(EmployeeDataErasureRequested message, CancellationToken cancellationToken)
    {
        await db.People.Where(p => p.EmployeeId == message.EmployeeId).ExecuteDeleteAsync(cancellationToken);
        await db.MainStoreCopies.Where(m => m.EmployeeId == message.EmployeeId).ExecuteDeleteAsync(cancellationToken);
        await db.ReportRecipients.Where(r => r.EmployeeId == message.EmployeeId).ExecuteDeleteAsync(cancellationToken);
        await permissions.EraseAsync(message.EmployeeId, cancellationToken);
    }
}

/// <summary>
/// Prepared (catalogue: Portal consumes the neutralised availability): the Portal API has no business function yet, so the message
/// is only acknowledged. TODO(Fachfunktion): store or forward when the Portal shows availability.
/// </summary>
public sealed class EmployeeAvailabilityChangedHandler : IMessageHandler<EmployeeAvailabilityChanged>
{
    public Task HandleAsync(EmployeeAvailabilityChanged message, CancellationToken cancellationToken) => Task.CompletedTask;
}
