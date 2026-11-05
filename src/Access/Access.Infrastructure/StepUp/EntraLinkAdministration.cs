using Access.Core;
using Access.Core.Acl;
using Access.Core.Publishing;
using Access.Infrastructure.Acl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Access.Infrastructure.StepUp;

/// <summary>
/// HR maintains which Microsoft account belongs to a person (for the second factor). Needs <see cref="AccessPrivileges.EntraLinkManage"/> with
/// reach over the person. A changed user name keeps the bound object id, removing the link drops it (and every pending or valid confirmation).
/// Every change is logged with the ids of the actor and the person only, never the user name.
/// </summary>
public sealed partial class EntraLinkAdministration(
    AccessDbContext db, IAclReader reader, TimeProvider time, AccessOptions options, ILogger<EntraLinkAdministration> logger)
{
    private const int MaxLength = 320;

    public async Task<AdminResult> SetAsync(Guid actor, Guid employeeId, string? userPrincipalName, CancellationToken cancellationToken)
    {
        var name = (userPrincipalName ?? "").Trim();
        if (!IsPlausible(name))
        {
            return AdminResult.Denied(GuardReason.Invalid);
        }

        if (!await db.People.AnyAsync(p => p.EmployeeId == employeeId, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = await Guard(actor, employeeId, cancellationToken);
        if (!guard.IsAllowed)
        {
            return new AdminResult(guard);
        }

        // One Microsoft account belongs to one person (compared without case; the table is as small as the staff).
        var others = await db.EntraLinks.AsNoTracking().Where(l => l.EmployeeId != employeeId).Select(l => l.UserPrincipalName).ToListAsync(cancellationToken);
        if (others.Any(other => string.Equals(other, name, StringComparison.OrdinalIgnoreCase)))
        {
            return AdminResult.Denied(GuardReason.InUse);
        }

        var link = await db.EntraLinks.FirstOrDefaultAsync(l => l.EmployeeId == employeeId, cancellationToken);
        if (link is null)
        {
            link = new EntraLinkRecord { EmployeeId = employeeId, UserPrincipalName = name };
            db.EntraLinks.Add(link);
        }

        link.UserPrincipalName = name;
        link.LinkedBy = actor;
        link.LinkedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        LogSet(actor, employeeId);
        return new AdminResult(GuardResult.Allowed);
    }

    public async Task<AdminResult> RemoveAsync(Guid actor, Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await db.People.AnyAsync(p => p.EmployeeId == employeeId, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = await Guard(actor, employeeId, cancellationToken);
        if (!guard.IsAllowed)
        {
            return new AdminResult(guard);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var removed = await db.EntraLinks.Where(l => l.EmployeeId == employeeId).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        await db.StepUpConfirmations.Where(c => c.EmployeeId == employeeId).ExecuteDeleteAsync(cancellationToken);
        await db.StepUpRequests.Where(r => r.EmployeeId == employeeId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogRemoved(actor, employeeId);
        return new AdminResult(GuardResult.Allowed);
    }

    private async Task<GuardResult> Guard(Guid actor, Guid employeeId, CancellationToken cancellationToken) =>
        new AclGuard(await reader.LoadAsync(cancellationToken), time.GetUtcNow(), options.Zone).CanManageEntraLink(actor, employeeId);

    /// <summary>A user principal name looks like an e-mail address: one <c>@</c> with text on both sides, no blanks.</summary>
    private static bool IsPlausible(string name)
    {
        var at = name.IndexOf('@', StringComparison.Ordinal);
        return name.Length is > 0 and <= MaxLength
            && at > 0 && at < name.Length - 1 && name.IndexOf('@', at + 1) < 0
            && !name.Any(char.IsWhiteSpace) && !name.Any(char.IsControl);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Entra link set by {Actor} for {EmployeeId}")]
    private partial void LogSet(Guid actor, Guid employeeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Entra link removed by {Actor} for {EmployeeId}")]
    private partial void LogRemoved(Guid actor, Guid employeeId);
}
