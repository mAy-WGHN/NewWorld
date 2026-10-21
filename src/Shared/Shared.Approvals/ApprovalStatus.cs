namespace Shared.Approvals;

/// <summary>Lifecycle: Requested → InReview → Approved | Rejected; Withdrawn while open.</summary>
public enum ApprovalStatus
{
    Unknown = 0,
    Requested = 1,
    InReview = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5,
}
