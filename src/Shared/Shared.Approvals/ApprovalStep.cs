namespace Shared.Approvals;

/// <summary>One link of the approval chain.</summary>
public sealed class ApprovalStep
{
    private ApprovalStep()
    {
    }

    internal ApprovalStep(int order, Guid approverId)
    {
        Id = Guid.NewGuid();
        Order = order;
        ApproverId = approverId;
    }

    public Guid Id { get; private set; }

    /// <summary>1-based position in the chain.</summary>
    public int Order { get; private set; }

    /// <summary>EmployeeId of the approver.</summary>
    public Guid ApproverId { get; private set; }

    public ApprovalDecision? Decision { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    internal void Decide(ApprovalDecision decision, DateTimeOffset at)
    {
        Decision = decision;
        DecidedAt = at;
    }
}
