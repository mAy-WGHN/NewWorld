using Access.Contracts;
using Access.Core;
using Access.Core.Acl;
using Access.Core.Publishing;
using Access.Infrastructure.Acl;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Access.Infrastructure.Publishing;

/// <summary>
/// Saves pending changes, recalculates everybody and publishes what changed, all in one transaction (outbox). Runs are
/// serialised with a PostgreSQL advisory lock, so two runs never hand out the same version for different states.
/// Access keeps its own permission copy like every other API and updates it from the published states.
/// </summary>
public sealed class RecalculationRunner(
    AccessDbContext db,
    Recalculator recalculator,
    IPermissionCache cache,
    IPermissionStore store,
    TimeProvider time,
    AccessOptions options)
{
    private const long AdvisoryLockKey = 0x4163636573735243; // "AccessRC"

    /// <summary>
    /// Runs <paramref name="change"/> (optional) and the recalculation in one transaction; joins an ambient transaction
    /// (e.g. the inbox transaction of a message handler) if there is one.
    /// </summary>
    public async Task<RecalculationResult> RunAsync(Func<CancellationToken, Task>? change, CancellationToken cancellationToken)
    {
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);
            if (change is not null)
            {
                await change(cancellationToken);
            }

            await EnsureBootstrapAdministratorsAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            var result = await recalculator.RecalculateAllAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var message in result.Permissions)
            {
                await store.SaveAsync(ToSnapshot(message), cancellationToken);
            }

            if (own is not null)
            {
                await own.CommitAsync(cancellationToken);
            }

            foreach (var message in result.Permissions)
            {
                cache.Apply(ToSnapshot(message));
            }

            return result;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    public static PermissionSnapshot ToSnapshot(PermissionsChanged message) => new(
        message.EmployeeId,
        message.Version,
        message.Privileges.ToDictionary(
            p => p.Key,
            p => p.Unrestricted ? PrivilegeReach.Everywhere : PrivilegeReach.Stores(p.StoreIds, p.EmployeeIds),
            StringComparer.Ordinal));

    /// <summary>Configured first administrators get the built-in administrator role permanently once their copy exists.</summary>
    private async Task EnsureBootstrapAdministratorsAsync(CancellationToken cancellationToken)
    {
        if (options.BootstrapAdministrators.Count == 0)
        {
            return;
        }

        var people = await db.People.Where(p => options.BootstrapAdministrators.Contains(p.PersonnelNumber)).Select(p => p.EmployeeId).ToListAsync(cancellationToken);
        foreach (var employeeId in people)
        {
            var hasOne = await db.Assignments.AnyAsync(a => a.EmployeeId == employeeId && a.RoleId == Role.AdministratorId && a.Source == nameof(AssignmentSource.Bootstrap), cancellationToken)
                         || db.Assignments.Local.Any(a => a.EmployeeId == employeeId && a.RoleId == Role.AdministratorId && a.Source == nameof(AssignmentSource.Bootstrap));
            if (!hasOne)
            {
                db.Assignments.Add(new AssignmentRecord
                {
                    Id = Guid.CreateVersion7(time.GetUtcNow()),
                    EmployeeId = employeeId,
                    RoleId = Role.AdministratorId,
                    Unrestricted = true,
                    Source = nameof(AssignmentSource.Bootstrap),
                });
            }
        }
    }
}
