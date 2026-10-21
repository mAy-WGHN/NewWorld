namespace Shared.Approvals;

public sealed class ApprovalException(ApprovalError error) : Exception($"Approval transition refused: {error}")
{
    public ApprovalError Error { get; } = error;
}
