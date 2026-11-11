using Portal.Core.Logbook;

namespace Portal.Core.Tests;

/// <summary>
/// Who sees which comment of a weekly report, who may set which state, and what a reply does to the state (decision 49). The matrix: the store manager sees
/// the replies of the regional leaders and no internal comments; a regional leader sees the replies of regional and district leaders and only the own internal
/// comments; a district leader sees everything.
/// </summary>
public sealed class LogbookCommentRulesTests
{
    private static readonly Guid Me = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private static LogbookComment Comment(LogbookCommentKind kind, LogbookLevel level, Guid? author = null) =>
        new(Guid.NewGuid(), kind, level, author ?? Other, "Text", DateTimeOffset.UnixEpoch);

    private static bool Sees(LogbookComment comment, LogbookLevel viewerLevel, bool storeManager = false, Guid? viewer = null) =>
        LogbookCommentRules.CanSee(comment, viewer ?? Me, viewerLevel, storeManager);

    // ---------------------------------------------------------------- the store manager

    [Fact]
    public void The_store_manager_sees_the_replies_of_the_regional_leaders_and_nothing_else()
    {
        Sees(Comment(LogbookCommentKind.Reply, LogbookLevel.Regional), LogbookLevel.None, storeManager: true).ShouldBeTrue();
        Sees(Comment(LogbookCommentKind.Reply, LogbookLevel.District), LogbookLevel.None, storeManager: true).ShouldBeFalse(); // the regional leader passes it on
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.Regional), LogbookLevel.None, storeManager: true).ShouldBeFalse();
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.District), LogbookLevel.None, storeManager: true).ShouldBeFalse();
    }

    [Fact]
    public void Even_the_own_internal_comment_is_not_visible_to_somebody_who_is_only_a_store_manager()
    {
        // e.g. somebody who was a leader once and wrote an internal comment, and is now only a store manager of the store
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.Regional, author: Me), LogbookLevel.None, storeManager: true).ShouldBeFalse();
    }

    [Fact]
    public void Somebody_who_is_neither_a_leader_nor_a_store_manager_of_the_store_sees_nothing()
    {
        foreach (var kind in new[] { LogbookCommentKind.Reply, LogbookCommentKind.Internal })
        {
            foreach (var level in new[] { LogbookLevel.Regional, LogbookLevel.District })
            {
                Sees(Comment(kind, level), LogbookLevel.None).ShouldBeFalse();
            }
        }
    }

    // ---------------------------------------------------------------- the regional leader

    [Fact]
    public void A_regional_leader_sees_all_replies_and_only_the_own_internal_comments()
    {
        Sees(Comment(LogbookCommentKind.Reply, LogbookLevel.Regional), LogbookLevel.Regional).ShouldBeTrue(); // also of the other regional leader of the region
        Sees(Comment(LogbookCommentKind.Reply, LogbookLevel.District), LogbookLevel.Regional).ShouldBeTrue(); // the answer of the district leader is meant for them
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.Regional, author: Me), LogbookLevel.Regional).ShouldBeTrue();
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.Regional, author: Other), LogbookLevel.Regional).ShouldBeFalse();
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.District, author: Other), LogbookLevel.Regional).ShouldBeFalse();
    }

    [Fact]
    public void An_internal_comment_of_the_district_leader_is_never_seen_by_a_regional_leader_even_if_the_ids_were_mixed_up()
    {
        // the author level counts too: a comment written as district leader is not "own" for a viewer who is only a regional leader now
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.District, author: Me), LogbookLevel.Regional).ShouldBeFalse();
    }

    // ---------------------------------------------------------------- the district leader

    [Fact]
    public void A_district_leader_sees_every_comment()
    {
        foreach (var kind in new[] { LogbookCommentKind.Reply, LogbookCommentKind.Internal })
        {
            foreach (var level in new[] { LogbookLevel.Regional, LogbookLevel.District })
            {
                Sees(Comment(kind, level), LogbookLevel.District).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void Somebody_who_is_a_regional_leader_and_the_store_manager_sees_what_both_roles_see()
    {
        Sees(Comment(LogbookCommentKind.Reply, LogbookLevel.Regional), LogbookLevel.Regional, storeManager: true).ShouldBeTrue();
        Sees(Comment(LogbookCommentKind.Internal, LogbookLevel.Regional, author: Other), LogbookLevel.Regional, storeManager: true).ShouldBeFalse(); // being store manager gives no internal comments
    }

    [Fact]
    public void The_visible_comments_keep_their_order()
    {
        var first = Comment(LogbookCommentKind.Reply, LogbookLevel.Regional);
        var hidden = Comment(LogbookCommentKind.Internal, LogbookLevel.District);
        var second = Comment(LogbookCommentKind.Reply, LogbookLevel.Regional);

        LogbookCommentRules.VisibleTo([first, hidden, second], Me, LogbookLevel.None, storeManager: true).ShouldBe([first, second]);
    }

    // ---------------------------------------------------------------- who may write what

    [Fact]
    public void Only_leaders_write_comments_and_only_regional_and_district_leaders_have_a_level()
    {
        LogbookCommentRules.CanWrite(LogbookLevel.None).ShouldBeFalse();
        LogbookCommentRules.CanWrite(LogbookLevel.Regional).ShouldBeTrue();
        LogbookCommentRules.CanWrite(LogbookLevel.District).ShouldBeTrue();
    }

    [Fact]
    public void The_text_of_a_comment_needs_more_than_spaces_and_has_a_limit()
    {
        LogbookCommentRules.TextProblem("  ").ShouldBe(LogbookCommentError.TextMissing);
        LogbookCommentRules.TextProblem(null).ShouldBe(LogbookCommentError.TextMissing);
        LogbookCommentRules.TextProblem(new string('x', LogbookLimits.MaxText + 1)).ShouldBe(LogbookCommentError.TextTooLong);
        LogbookCommentRules.TextProblem(new string('x', LogbookLimits.MaxText)).ShouldBe(LogbookCommentError.None);
    }

    // ---------------------------------------------------------------- the state

    [Fact]
    public void A_reply_of_a_regional_leader_puts_the_report_in_progress_again_but_nothing_else_changes_the_state()
    {
        foreach (var state in new[] { LogbookReportState.New, LogbookReportState.FollowedUp, LogbookReportState.Done, LogbookReportState.InProgress })
        {
            LogbookStateRules.AfterComment(state, LogbookCommentKind.Reply, LogbookLevel.Regional).ShouldBe(LogbookReportState.InProgress);
            LogbookStateRules.AfterComment(state, LogbookCommentKind.Reply, LogbookLevel.District).ShouldBe(state); // the district leader answers the regional leaders
            LogbookStateRules.AfterComment(state, LogbookCommentKind.Internal, LogbookLevel.Regional).ShouldBe(state);
            LogbookStateRules.AfterComment(state, LogbookCommentKind.Internal, LogbookLevel.District).ShouldBe(state);
        }
    }

    [Fact]
    public void A_leader_sets_the_state_to_in_progress_or_done_never_to_new_or_followed_up()
    {
        LogbookStateRules.CanSet(LogbookReportState.New, LogbookReportState.InProgress).ShouldBeTrue();
        LogbookStateRules.CanSet(LogbookReportState.InProgress, LogbookReportState.Done).ShouldBeTrue();
        LogbookStateRules.CanSet(LogbookReportState.FollowedUp, LogbookReportState.Done).ShouldBeTrue();
        LogbookStateRules.CanSet(LogbookReportState.FollowedUp, LogbookReportState.InProgress).ShouldBeTrue();
        LogbookStateRules.CanSet(LogbookReportState.Done, LogbookReportState.InProgress).ShouldBeTrue(); // reopen
        LogbookStateRules.CanSet(LogbookReportState.New, LogbookReportState.Done).ShouldBeTrue(); // nothing to do about it
        LogbookStateRules.CanSet(LogbookReportState.InProgress, LogbookReportState.New).ShouldBeFalse();
        LogbookStateRules.CanSet(LogbookReportState.InProgress, LogbookReportState.FollowedUp).ShouldBeFalse(); // only the store manager does that
        LogbookStateRules.CanSet(LogbookReportState.Done, LogbookReportState.Unknown).ShouldBeFalse();
    }

    [Fact]
    public void The_store_manager_can_mark_a_report_as_followed_up_unless_it_is_done()
    {
        LogbookStateRules.CanFollowUp(LogbookReportState.New).ShouldBeTrue();
        LogbookStateRules.CanFollowUp(LogbookReportState.InProgress).ShouldBeTrue();
        LogbookStateRules.CanFollowUp(LogbookReportState.FollowedUp).ShouldBeTrue(); // update the note
        LogbookStateRules.CanFollowUp(LogbookReportState.Done).ShouldBeFalse();
    }

    [Fact]
    public void The_list_puts_followed_up_reports_first_then_new_then_in_progress_then_done()
    {
        LogbookStateRules.Priority(LogbookReportState.FollowedUp).ShouldBeLessThan(LogbookStateRules.Priority(LogbookReportState.New));
        LogbookStateRules.Priority(LogbookReportState.New).ShouldBeLessThan(LogbookStateRules.Priority(LogbookReportState.InProgress));
        LogbookStateRules.Priority(LogbookReportState.InProgress).ShouldBeLessThan(LogbookStateRules.Priority(LogbookReportState.Done));
    }

    // ---------------------------------------------------------------- revised since the reply

    [Fact]
    public void A_report_is_revised_since_the_reply_if_it_was_changed_after_the_last_reply_of_the_viewer()
    {
        var reply = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

        LogbookCommentRules.RevisedSinceReply(reply.AddHours(1), reply).ShouldBeTrue();
        LogbookCommentRules.RevisedSinceReply(reply.AddHours(-1), reply).ShouldBeFalse(); // changed before the reply
        LogbookCommentRules.RevisedSinceReply(reply, reply).ShouldBeFalse();
        LogbookCommentRules.RevisedSinceReply(null, reply).ShouldBeFalse(); // never revised
        LogbookCommentRules.RevisedSinceReply(reply.AddHours(1), null).ShouldBeFalse(); // no reply of the viewer yet: nothing to be "since"
    }
}
