namespace Shared.Approvals;

/// <summary>Reason a transition was refused; callers map it to a business error.</summary>
public enum ApprovalError
{
    Unknown = 0,
    NoApprovers = 1,
    RequesterIsApprover = 2,
    DuplicateApprover = 3,
    NotCurrentApprover = 4,
    NotRequester = 5,
    AlreadyClosed = 6,
    AlreadyInReview = 7,
}
