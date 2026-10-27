using Access.Core.Responsibilities;
using Access.Infrastructure.Publishing;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;

namespace Access.Infrastructure.People;

/// <summary>Copies of people from BC Integration; every change recalculates permissions (exit takes effect at the exit date).</summary>
public sealed class EmployeeChangedHandler(AccessDbContext db, RecalculationRunner runner) : IMessageHandler<EmployeeChanged>
{
    public Task HandleAsync(EmployeeChanged message, CancellationToken cancellationToken) =>
        runner.RunAsync(async ct =>
        {
            var person = await db.People.FindAsync([message.EmployeeId], ct);
            if (person is null)
            {
                person = new PersonRecord { EmployeeId = message.EmployeeId, PersonnelNumber = message.PersonnelNumber, Name = message.Name };
                db.People.Add(person);
            }

            person.PersonnelNumber = message.PersonnelNumber;
            person.Name = message.Name;
            person.HomeStoreId = message.HomeStoreId;
            person.Position = message.Position;
            person.ExitDate = message.ExitDate;
        }, cancellationToken);
}

public sealed class EmployeeHiredHandler(AccessDbContext db, RecalculationRunner runner) : IMessageHandler<EmployeeHired>
{
    /// <summary>The full state comes with <see cref="EmployeeChanged"/>; here only the home store of a known person is updated.</summary>
    public Task HandleAsync(EmployeeHired message, CancellationToken cancellationToken) =>
        runner.RunAsync(async ct =>
        {
            if (await db.People.FindAsync([message.EmployeeId], ct) is { } person)
            {
                person.HomeStoreId = message.HomeStoreId;
            }
        }, cancellationToken);
}

public sealed class EmployeeLeftHandler(AccessDbContext db, RecalculationRunner runner) : IMessageHandler<EmployeeLeft>
{
    public Task HandleAsync(EmployeeLeft message, CancellationToken cancellationToken) =>
        runner.RunAsync(async ct =>
        {
            if (await db.People.FindAsync([message.EmployeeId], ct) is { } person)
            {
                person.ExitDate = message.ExitDate;
            }
        }, cancellationToken);
}

public sealed class StoreChangedHandler(AccessDbContext db) : IMessageHandler<StoreChanged>
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

/// <summary>
/// Deletes everything about the person: copy, stored PIN, sessions, assignments, responsibilities held or targeting them, publication state and the
/// own permission copy. Other people's reach and responsibilities are recalculated.
/// </summary>
public sealed class EmployeeDataErasureRequestedHandler(AccessDbContext db, RecalculationRunner runner, PermissionsChangedHandler permissions)
    : IMessageHandler<EmployeeDataErasureRequested>
{
    public async Task HandleAsync(EmployeeDataErasureRequested message, CancellationToken cancellationToken)
    {
        var id = message.EmployeeId;
        await runner.RunAsync(async ct =>
        {
            await db.Assignments.Where(a => a.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.Responsibilities.Where(r => r.ResponsibleEmployeeId == id || (r.TargetKind == nameof(ResponsibilityTarget.Employee) && r.TargetId == id)).ExecuteDeleteAsync(ct);
            await db.Publications.Where(p => p.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.Credentials.Where(c => c.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.RefreshTokens.Where(t => t.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.MainStoreOverrides.Where(m => m.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.MainStoreOverrides.Where(m => m.SetBy == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.SetBy, Guid.Empty), ct);
            await db.People.Where(p => p.EmployeeId == id).ExecuteDeleteAsync(ct);
        }, cancellationToken);
        await permissions.EraseAsync(id, cancellationToken);
    }
}
