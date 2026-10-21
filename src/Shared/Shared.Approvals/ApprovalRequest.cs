namespace Shared.Approvals;

/// <summary>
/// Approval of one subject (e.g. a vacation request) by a sequential chain of approvers. Pure state machine:
/// time is passed in (from <see cref="TimeProvider"/>), nothing is persisted here. The including API stores it
/// in its own database via <see cref="ApprovalModelBuilderExtensions.ConfigureApprovals"/>.
/// </summary>
public sealed class ApprovalRequest
{
    private readonly List<ApprovalStep> _steps = [];

    private ApprovalRequest()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Kind of the subject, e.g. <c>Vacation</c>; owned by the including API.</summary>
    public string SubjectType { get; private set; } = "";

    public Guid SubjectId { get; private set; }

    /// <summary>EmployeeId of the requester.</summary>
    public Guid RequesterId { get; private set; }

    public ApprovalStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when the request reached a terminal state.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Chain in order; sorted here because the database does not guarantee load order.</summary>
    public IReadOnlyList<ApprovalStep> Steps => _steps.OrderBy(s => s.Order).ToList();

    /// <summary>The approver whose decision is awaited, <c>null</c> once the request is closed.</summary>
    public Guid? CurrentApproverId => IsClosed ? null : CurrentStep?.ApproverId;

    private bool IsClosed => Status is ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.Withdrawn;

    private ApprovalStep? CurrentStep => _steps.OrderBy(s => s.Order).FirstOrDefault(s => s.Decision is null);

    public static ApprovalRequest Create(
        string subjectType,
        Guid subjectId,
        Guid requesterId,
        IReadOnlyList<Guid> approverIds,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        if (approverIds.Count == 0)
        {
            throw new ApprovalException(ApprovalError.NoApprovers);
        }

        if (approverIds.Contains(requesterId))
        {
            throw new ApprovalException(ApprovalError.RequesterIsApprover);
        }

        if (approverIds.Distinct().Count() != approverIds.Count)
        {
            throw new ApprovalException(ApprovalError.DuplicateApprover);
        }

        var request = new ApprovalRequest
        {
            Id = Guid.NewGuid(),
            SubjectType = subjectType,
            SubjectId = subjectId,
            RequesterId = requesterId,
            Status = ApprovalStatus.Requested,
            CreatedAt = now,
        };
        for (var i = 0; i < approverIds.Count; i++)
        {
            request._steps.Add(new ApprovalStep(i + 1, approverIds[i]));
        }

        return request;
    }

    /// <summary>The current approver opened the request: Requested → InReview.</summary>
    public void BeginReview(Guid approverId, DateTimeOffset now)
    {
        _ = now;
        EnsureOpen();
        EnsureCurrentApprover(approverId);
        if (Status == ApprovalStatus.InReview)
        {
            throw new ApprovalException(ApprovalError.AlreadyInReview);
        }

        Status = ApprovalStatus.InReview;
    }

    public void Approve(Guid approverId, DateTimeOffset now)
    {
        Decide(approverId, ApprovalDecision.Approved, now);
        if (_steps.All(s => s.Decision is not null))
        {
            Close(ApprovalStatus.Approved, now);
        }
        else
        {
            Status = ApprovalStatus.InReview;
        }
    }

    public void Reject(Guid approverId, DateTimeOffset now)
    {
        Decide(approverId, ApprovalDecision.Rejected, now);
        Close(ApprovalStatus.Rejected, now);
    }

    public void Withdraw(Guid requesterId, DateTimeOffset now)
    {
        EnsureOpen();
        if (requesterId != RequesterId)
        {
            throw new ApprovalException(ApprovalError.NotRequester);
        }

        Close(ApprovalStatus.Withdrawn, now);
    }

    private ApprovalStep Decide(Guid approverId, ApprovalDecision decision, DateTimeOffset now)
    {
        EnsureOpen();
        EnsureCurrentApprover(approverId);
        var step = CurrentStep!;
        step.Decide(decision, now);
        return step;
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new ApprovalException(ApprovalError.AlreadyClosed);
        }
    }

    private void EnsureCurrentApprover(Guid approverId)
    {
        if (CurrentApproverId != approverId)
        {
            throw new ApprovalException(ApprovalError.NotCurrentApprover);
        }
    }

    private void Close(ApprovalStatus status, DateTimeOffset now)
    {
        Status = status;
        DecidedAt = now;
    }
}
