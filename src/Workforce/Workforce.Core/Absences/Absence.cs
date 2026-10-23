using Workforce.Contracts;

namespace Workforce.Core.Absences;

/// <summary>State of the transfer to Business Central; the owner of the absence keeps it, updated by BcSyncSucceeded/BcSyncFailed.</summary>
public enum SyncStatus
{
    Unknown = 0,
    Pending = 1,
    Synced = 2,
    Failed = 3,
}

/// <summary>
/// An absence of a person: sickness (period, "AU liegt vor" yes/no, no free text and no diagnosis) or vacation (approved through
/// <c>Shared.Approvals</c>). Every change raises <see cref="Version"/>; the version goes to BC and makes the transfer an upsert.
/// </summary>
public sealed class Absence
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    public AbsenceType Type { get; init; }

    public DateOnly From { get; set; }

    /// <summary>Last day; <c>null</c> = until further notice (sickness only).</summary>
    public DateOnly? To { get; set; }

    public AbsenceStatus Status { get; set; }

    /// <summary>Sickness only: a medical certificate (AU) is on file. Nothing else about the illness is stored.</summary>
    public bool? CertificateOnFile { get; init; }

    public long Version { get; set; }

    /// <summary>Vacation only: the approval of this request (a split keeps the approval of the original).</summary>
    public Guid? ApprovalRequestId { get; set; }

    public SyncStatus SyncStatus { get; set; }

    public long? SyncedVersion { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateRange Range => new(From, To);

    /// <summary>Counts for overlaps, availability and the vacation account.</summary>
    public bool IsActive => Status is AbsenceStatus.Requested or AbsenceStatus.Approved or AbsenceStatus.Reported;

    /// <summary>Marks a change: new version, to be transferred to BC.</summary>
    public void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
        SyncStatus = SyncStatus.Pending;
    }

    /// <summary>Applies BC's answer; answers for outdated versions are ignored (a newer version is in flight).</summary>
    /// <returns><c>true</c> if the answer concerned the current version.</returns>
    public bool ApplySyncResult(long version, bool succeeded)
    {
        if (succeeded)
        {
            SyncedVersion = Math.Max(SyncedVersion ?? 0, version);
        }

        if (version != Version)
        {
            return false;
        }

        SyncStatus = succeeded ? SyncStatus.Synced : SyncStatus.Failed;
        return true;
    }
}
