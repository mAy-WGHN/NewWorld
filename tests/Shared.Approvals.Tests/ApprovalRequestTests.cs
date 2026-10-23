using Shared.Approvals;

namespace Shared.Approvals.Tests;

public class ApprovalRequestTests
{
    private static readonly Guid Requester = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Manager = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Director = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Outsider = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly DateTimeOffset T0 = new(2028, 2, 29, 23, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(2); // crosses midnight and the leap day

    private static ApprovalRequest NewRequest(params Guid[] approvers) =>
        ApprovalRequest.Create("Vacation", Guid.NewGuid(), Requester, approvers, T0);

    [Fact]
    public void New_request_is_requested_with_ordered_steps()
    {
        var request = NewRequest(Manager, Director);

        request.Status.ShouldBe(ApprovalStatus.Requested);
        request.CreatedAt.ShouldBe(T0);
        request.Steps.Select(s => s.ApproverIds.Single()).ShouldBe([Manager, Director]);
        request.Steps.Select(s => s.Order).ShouldBe([1, 2]);
        request.CurrentApproverIds.ShouldBe([Manager]);
    }

    [Fact]
    public void Create_requires_at_least_one_approver()
    {
        var ex = Should.Throw<ApprovalException>(() => NewRequest());
        ex.Error.ShouldBe(ApprovalError.NoApprovers);
    }

    [Fact]
    public void Requester_cannot_approve_own_request()
    {
        var ex = Should.Throw<ApprovalException>(() => NewRequest(Requester));
        ex.Error.ShouldBe(ApprovalError.RequesterIsApprover);
    }

    [Fact]
    public void Begin_review_moves_requested_to_in_review()
    {
        var request = NewRequest(Manager);

        request.BeginReview(Manager, T1);

        request.Status.ShouldBe(ApprovalStatus.InReview);
    }

    [Fact]
    public void Approving_the_only_step_approves_the_request()
    {
        var request = NewRequest(Manager);

        request.Approve(Manager, T1);

        request.Status.ShouldBe(ApprovalStatus.Approved);
        request.CurrentApproverIds.ShouldBeEmpty();
        request.Steps.Single().DecidedAt.ShouldBe(T1);
        request.Steps.Single().Decision.ShouldBe(ApprovalDecision.Approved);
        request.DecidedAt.ShouldBe(T1);
    }

    [Fact]
    public void Approving_a_non_final_step_keeps_the_request_in_review_and_advances_the_chain()
    {
        var request = NewRequest(Manager, Director);

        request.Approve(Manager, T1);

        request.Status.ShouldBe(ApprovalStatus.InReview);
        request.CurrentApproverIds.ShouldBe([Director]);
        request.DecidedAt.ShouldBeNull();

        request.Approve(Director, T1.AddDays(1));
        request.Status.ShouldBe(ApprovalStatus.Approved);
    }

    [Fact]
    public void Rejecting_any_step_rejects_the_request_and_stops_the_chain()
    {
        var request = NewRequest(Manager, Director);

        request.Reject(Manager, T1);

        request.Status.ShouldBe(ApprovalStatus.Rejected);
        request.CurrentApproverIds.ShouldBeEmpty();
        request.Steps[1].Decision.ShouldBeNull();
    }

    [Fact]
    public void Only_the_current_approver_may_decide()
    {
        var request = NewRequest(Manager, Director);

        Should.Throw<ApprovalException>(() => request.Approve(Director, T1)).Error.ShouldBe(ApprovalError.NotCurrentApprover);
        Should.Throw<ApprovalException>(() => request.Reject(Outsider, T1)).Error.ShouldBe(ApprovalError.NotCurrentApprover);
        Should.Throw<ApprovalException>(() => request.BeginReview(Outsider, T1)).Error.ShouldBe(ApprovalError.NotCurrentApprover);
        request.Status.ShouldBe(ApprovalStatus.Requested);
    }

    [Fact]
    public void Requester_can_withdraw_while_open()
    {
        var requested = NewRequest(Manager);
        requested.Withdraw(Requester, T1);
        requested.Status.ShouldBe(ApprovalStatus.Withdrawn);

        var inReview = NewRequest(Manager, Director);
        inReview.Approve(Manager, T1);
        inReview.Withdraw(Requester, T1.AddHours(1));
        inReview.Status.ShouldBe(ApprovalStatus.Withdrawn);
        inReview.CurrentApproverIds.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_requester_can_withdraw()
    {
        var request = NewRequest(Manager);

        Should.Throw<ApprovalException>(() => request.Withdraw(Manager, T1)).Error.ShouldBe(ApprovalError.NotRequester);
    }

    [Theory]
    [InlineData(ApprovalStatus.Approved)]
    [InlineData(ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.Withdrawn)]
    public void Terminal_states_accept_no_further_transitions(ApprovalStatus terminal)
    {
        var request = NewRequest(Manager);
        switch (terminal)
        {
            case ApprovalStatus.Approved: request.Approve(Manager, T1); break;
            case ApprovalStatus.Rejected: request.Reject(Manager, T1); break;
            default: request.Withdraw(Requester, T1); break;
        }

        request.Status.ShouldBe(terminal);
        Should.Throw<ApprovalException>(() => request.Approve(Manager, T1)).Error.ShouldBe(ApprovalError.AlreadyClosed);
        Should.Throw<ApprovalException>(() => request.Reject(Manager, T1)).Error.ShouldBe(ApprovalError.AlreadyClosed);
        Should.Throw<ApprovalException>(() => request.BeginReview(Manager, T1)).Error.ShouldBe(ApprovalError.AlreadyClosed);
        Should.Throw<ApprovalException>(() => request.Withdraw(Requester, T1)).Error.ShouldBe(ApprovalError.AlreadyClosed);
        request.Status.ShouldBe(terminal);
    }

    [Fact]
    public void Begin_review_twice_is_rejected()
    {
        var request = NewRequest(Manager);
        request.BeginReview(Manager, T1);

        Should.Throw<ApprovalException>(() => request.BeginReview(Manager, T1)).Error.ShouldBe(ApprovalError.AlreadyInReview);
    }

    [Fact]
    public void Same_person_may_not_appear_twice_in_the_chain()
    {
        Should.Throw<ApprovalException>(() => NewRequest(Manager, Manager)).Error.ShouldBe(ApprovalError.DuplicateApprover);
    }

    [Fact]
    public void Alternatives_of_a_step_any_one_of_them_decides()
    {
        var request = ApprovalRequest.CreateWithAlternatives("Vacation", Guid.NewGuid(), Requester, [[Manager, Outsider], [Director]], T0);

        request.CurrentApproverIds.ShouldBe([Manager, Outsider], ignoreOrder: true);
        request.Approve(Outsider, T1); // the substitute decides the first step

        request.Steps[0].DecidedBy.ShouldBe(Outsider);
        request.CurrentApproverIds.ShouldBe([Director]);
        Should.Throw<ApprovalException>(() => request.Approve(Manager, T1)).Error.ShouldBe(ApprovalError.NotCurrentApprover); // step already decided
    }

    [Fact]
    public void Any_alternative_may_reject_or_begin_review()
    {
        var rejected = ApprovalRequest.CreateWithAlternatives("Vacation", Guid.NewGuid(), Requester, [[Manager, Outsider]], T0);
        rejected.Reject(Manager, T1);
        rejected.Status.ShouldBe(ApprovalStatus.Rejected);

        var review = ApprovalRequest.CreateWithAlternatives("Vacation", Guid.NewGuid(), Requester, [[Manager, Outsider]], T0);
        review.BeginReview(Outsider, T1);
        review.Status.ShouldBe(ApprovalStatus.InReview);
    }

    [Fact]
    public void Alternatives_follow_the_same_integrity_rules()
    {
        Should.Throw<ApprovalException>(() => ApprovalRequest.CreateWithAlternatives("V", Guid.NewGuid(), Requester, [[]], T0)).Error.ShouldBe(ApprovalError.NoApprovers);
        Should.Throw<ApprovalException>(() => ApprovalRequest.CreateWithAlternatives("V", Guid.NewGuid(), Requester, [[Manager, Requester]], T0)).Error.ShouldBe(ApprovalError.RequesterIsApprover);
        Should.Throw<ApprovalException>(() => ApprovalRequest.CreateWithAlternatives("V", Guid.NewGuid(), Requester, [[Manager], [Manager, Director]], T0)).Error.ShouldBe(ApprovalError.DuplicateApprover);
    }
}
