using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;
using Workforce.Core.Absences;

namespace Workforce.Infrastructure.People;

public sealed class EmployeeChangedHandler(WorkforceDbContext db) : IMessageHandler<EmployeeChanged>
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
        person.HomeStoreId = message.HomeStoreId;
        person.ExitDate = message.ExitDate;
    }
}

/// <summary>Records the exit date and ends or cancels the absences after it.</summary>
public sealed class EmployeeLeftHandler(WorkforceDbContext db, AbsenceService absences) : IMessageHandler<EmployeeLeft>
{
    public async Task HandleAsync(EmployeeLeft message, CancellationToken cancellationToken)
    {
        if (await db.People.FindAsync([message.EmployeeId], cancellationToken) is { } person)
        {
            person.ExitDate = message.ExitDate;
        }

        await absences.OnEmployeeLeftAsync(message.EmployeeId, message.ExitDate, cancellationToken);
    }
}

public sealed class StoreChangedHandler(WorkforceDbContext db) : IMessageHandler<StoreChanged>
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

/// <summary>Keeps the copy of who approves absences of a person (topic <c>absence.approval</c>); older versions are ignored.</summary>
public sealed class ResponsibilitiesChangedHandler(WorkforceDbContext db) : IMessageHandler<ResponsibilitiesChanged>
{
    public async Task HandleAsync(ResponsibilitiesChanged message, CancellationToken cancellationToken)
    {
        var copy = await db.ApproverCopies.FindAsync([message.EmployeeId], cancellationToken);
        if (copy is not null && message.Version <= copy.Version)
        {
            return;
        }

        if (copy is null)
        {
            copy = new ApproverCopyRecord { EmployeeId = message.EmployeeId };
            db.ApproverCopies.Add(copy);
        }

        copy.Version = message.Version;
        copy.ApproverIds = [.. message.Responsibilities.Where(r => r.Topic == ApproverCopyRecord.Topic).SelectMany(r => r.ResponsibleEmployeeIds).Distinct()];
    }
}

/// <summary>BC's answer for an absence. Only results for requests of Workforce count (the subscription is filtered on the source service).</summary>
public sealed class BcSyncSucceededHandler(WorkforceDbContext db) : IMessageHandler<BcSyncSucceeded>
{
    public async Task HandleAsync(BcSyncSucceeded message, CancellationToken cancellationToken)
    {
        if (message is { SourceService: SourceService.Workforce, EntityType: BcSyncEntityType.Absence }
            && await db.Absences.FindAsync([message.EntityId], cancellationToken) is { } absence)
        {
            absence.ApplySyncResult(message.Version, succeeded: true);
        }
    }
}

public sealed class BcSyncFailedHandler(WorkforceDbContext db) : IMessageHandler<BcSyncFailed>
{
    public async Task HandleAsync(BcSyncFailed message, CancellationToken cancellationToken)
    {
        if (message is { SourceService: SourceService.Workforce, EntityType: BcSyncEntityType.Absence }
            && await db.Absences.FindAsync([message.EntityId], cancellationToken) is { } absence)
        {
            absence.ApplySyncResult(message.Version, succeeded: false);
        }
    }
}

/// <summary>
/// Deletes everything about the person: absences, their approvals, entitlements, copies, the person's name in other approvals'
/// alternatives, outbox rows that mention the person and the permission copy.
/// </summary>
public sealed class EmployeeDataErasureRequestedHandler(WorkforceDbContext db, PermissionsChangedHandler permissions) : IMessageHandler<EmployeeDataErasureRequested>
{
    public async Task HandleAsync(EmployeeDataErasureRequested message, CancellationToken cancellationToken)
    {
        var id = message.EmployeeId;
        var needle = "%" + id.ToString() + "%";
        await db.Set<Shared.Approvals.ApprovalRequest>().Where(r => r.RequesterId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Absences.Where(a => a.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Entitlements.Where(e => e.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.People.Where(p => p.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.ApproverCopies.Where(a => a.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        foreach (var copy in await db.ApproverCopies.Where(a => a.ApproverIds.Contains(id)).ToListAsync(cancellationToken))
        {
            copy.ApproverIds = [.. copy.ApproverIds.Where(a => a != id)];
        }

        foreach (var step in await db.Set<Shared.Approvals.ApprovalStep>().Where(s => s.ApproverIds.Contains(id)).ToListAsync(cancellationToken))
        {
            step.ApproverIds.Remove(id);
        }

        await db.Set<Shared.Infrastructure.Messaging.OutboxMessage>().Where(o => EF.Functions.Like(o.Payload, needle)).ExecuteDeleteAsync(cancellationToken);
        await permissions.EraseAsync(id, cancellationToken);
    }
}
