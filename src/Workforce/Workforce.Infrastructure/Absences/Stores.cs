using Microsoft.EntityFrameworkCore;
using Shared.Approvals;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Workforce.Core.Absences;
using Workforce.Infrastructure.Certificates;
using Workforce.Infrastructure.People;

namespace Workforce.Infrastructure.Absences;

public sealed class EfAbsenceStore(WorkforceDbContext db) : IAbsenceStore
{
    /// <summary>Transaction-scoped advisory lock per person: parallel changes for the same person queue up. Needs a transaction (see <see cref="WorkforceUnitOfWork"/>).</summary>
    public async Task LockEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var key = BitConverter.ToInt64(employeeId.ToByteArray(), 0);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    public async Task<IReadOnlyList<Absence>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.Absences.Where(a => a.EmployeeId == employeeId).ToListAsync(cancellationToken);

    public async Task<Absence?> GetAsync(Guid absenceId, CancellationToken cancellationToken) =>
        await db.Absences.FirstOrDefaultAsync(a => a.Id == absenceId, cancellationToken);

    public void Add(Absence absence) => db.Absences.Add(absence);

    public async Task<ApprovalRequest?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken) =>
        await db.Set<ApprovalRequest>().Include(r => r.Steps).FirstOrDefaultAsync(r => r.Id == approvalId, cancellationToken);

    public void AddApproval(ApprovalRequest approval) => db.Set<ApprovalRequest>().Add(approval);
}

public sealed class EfPeopleDirectory(WorkforceDbContext db) : IPeopleDirectory
{
    public async Task<PersonInfo?> GetAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken) is { } p
            ? new PersonInfo(p.EmployeeId, p.Name, p.HomeStoreId, p.ExitDate, await StoreCityAsync(p.HomeStoreId, cancellationToken))
            : null;

    private async Task<string?> StoreCityAsync(Guid? storeId, CancellationToken cancellationToken) =>
        storeId is { } id ? await db.Stores.AsNoTracking().Where(s => s.StoreId == id).Select(s => s.City).FirstOrDefaultAsync(cancellationToken) : null;
}

public sealed class EfEntitlementStore(WorkforceDbContext db) : IEntitlementStore
{
    public async Task<decimal?> GetAsync(Guid employeeId, int year, CancellationToken cancellationToken) =>
        await db.Entitlements.AsNoTracking().Where(e => e.EmployeeId == employeeId && e.Year == year).Select(e => (decimal?)e.Days).FirstOrDefaultAsync(cancellationToken);

    public async Task SetAsync(Guid employeeId, int year, decimal days, CancellationToken cancellationToken)
    {
        var row = await db.Entitlements.FindAsync([employeeId, year], cancellationToken);
        if (row is null)
        {
            db.Entitlements.Add(new EntitlementRecord { EmployeeId = employeeId, Year = year, Days = days });
        }
        else
        {
            row.Days = days;
        }
    }
}

public sealed class EfResponsibilityDirectory(WorkforceDbContext db) : IResponsibilityDirectory
{
    public async Task<IReadOnlyList<Guid>> ApproversAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.ApproverCopies.AsNoTracking().Where(a => a.EmployeeId == employeeId).Select(a => a.ApproverIds).FirstOrDefaultAsync(cancellationToken) ?? [];
}

/// <summary>Publishes through the outbox: in the same transaction as the change.</summary>
public sealed class OutboxWorkforceMessages(Outbox<WorkforceDbContext> outbox) : IWorkforceMessages
{
    public void Publish(IEvent message) => outbox.Publish(message);

    public void Send(ICommand message) => outbox.Send(message);
}

/// <summary>
/// One transaction for a use case: change, outbox messages and the per-person lock commit (or roll back) together. Joins an ambient
/// transaction (the inbox transaction of a message handler) if there is one.
/// </summary>
public sealed class WorkforceUnitOfWork(WorkforceDbContext db, CertificateFileCleanup fileCleanup)
{
    public async Task<T> RunAsync<T>(Func<Task<T>> work, Func<T, bool> shouldCommit, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            var inner = await work();
            await db.SaveChangesAsync(cancellationToken);
            return inner;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work();
        if (shouldCommit(result))
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await fileCleanup.FlushAsync(cancellationToken); // files of deleted documents go only after the commit
        }
        else
        {
            db.ChangeTracker.Clear();
            fileCleanup.Discard();
        }

        return result;
    }

    public Task<AbsenceResult> RunAsync(Func<Task<AbsenceResult>> work, CancellationToken cancellationToken) =>
        RunAsync(work, r => r.Error == AbsenceError.None, cancellationToken);
}
