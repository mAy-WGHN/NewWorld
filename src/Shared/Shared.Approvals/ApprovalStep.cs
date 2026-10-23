namespace Shared.Approvals;

/// <summary>
/// One link of the approval chain. Several people can be alternatives (e.g. the store manager and a substitute): the first
/// decision of any of them decides the step.
/// </summary>
public sealed class ApprovalStep
{
    private ApprovalStep()
    {
    }

    internal ApprovalStep(int order, IReadOnlyList<Guid> approverIds)
    {
        Id = Guid.NewGuid();
        Order = order;
        ApproverIds = [.. approverIds];
    }

    public Guid Id { get; private set; }

    /// <summary>1-based position in the chain.</summary>
    public int Order { get; private set; }

    /// <summary>EmployeeIds of the alternatives; any one of them may decide.</summary>
    public List<Guid> ApproverIds { get; private set; } = [];

    public ApprovalDecision? Decision { get; private set; }

    /// <summary>Who decided (one of <see cref="ApproverIds"/>).</summary>
    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    internal void Decide(Guid approverId, ApprovalDecision decision, DateTimeOffset at)
    {
        Decision = decision;
        DecidedBy = approverId;
        DecidedAt = at;
    }
}
