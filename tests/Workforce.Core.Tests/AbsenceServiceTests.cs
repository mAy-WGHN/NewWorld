using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Shared.Approvals;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;

namespace Workforce.Core.Tests;

public class AbsenceServiceTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Tom = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-000000000017");

    private readonly FakeStore _store = new();
    private readonly FakePeople _people = new();
    private readonly FakeEntitlements _entitlements = new();
    private readonly FakeExtraVacation _extraVacation = new();
    private readonly FakeResponsibilities _responsibilities = new();
    private readonly RecordingMessages _messages = new();

    // Wednesday 2026-07-15, 10:00 local (Europe/Berlin, summer time)
    private FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero));

    public AbsenceServiceTests()
    {
        _people.People[Erika] = new PersonInfo(Erika, "Erika Mustermann", Store, null);
        _people.People[Max] = new PersonInfo(Max, "Max Muster", Store, null);
        _entitlements.Days[(Erika, 2026)] = 30;
        _entitlements.Days[(Erika, 2027)] = 30;
        _entitlements.Days[(Erika, 2028)] = 30;
        _responsibilities.Approvers[Erika] = [Max];
    }

    private AbsenceService Service(string? state = null) => new(
        _store, _people, _entitlements, _extraVacation, _responsibilities, _messages, _time,
        new WorkforceOptions { HolidayState = state }, new GermanHolidayCalendar());

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private async Task<Absence> ApprovedVacation(string from, string to)
    {
        var requested = await Service().RequestVacationAsync(Erika, D(from), D(to), CancellationToken.None);
        (await Service().DecideVacationAsync(requested.Absence!.Id, Max, approve: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        _messages.Sent.Clear();
        return requested.Absence;
    }

    // ---------------------------------------------------------------- sick note

    [Fact]
    public async Task Sick_note_is_reported_without_approval_and_creates_exactly_three_messages()
    {
        var result = await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.None);
        var absence = result.Absence!;
        absence.Status.ShouldBe(AbsenceStatus.Reported);
        absence.Type.ShouldBe(AbsenceType.Sickness);
        absence.Version.ShouldBe(1);
        absence.CertificateStatus.ShouldBe(CertificateStatus.Missing);
        absence.ApprovalRequestId.ShouldBeNull();
        _store.Approvals.ShouldBeEmpty();
        _messages.Sent.Count.ShouldBe(3);
        _messages.Of<SyncAbsenceToBc>().Single().ShouldMatch(absence, AbsenceType.Sickness, AbsenceStatus.Reported, 1);
        var availability = _messages.Of<EmployeeAvailabilityChanged>().Single();
        (availability.EmployeeId, availability.From, availability.To, availability.Status)
            .ShouldBe((Erika, D("2026-07-15"), D("2026-07-17"), AvailabilityStatus.Unavailable));
        availability.OccurredAt.ShouldBe(_time.GetUtcNow());
        var notification = _messages.Of<SendNotification>().Single();
        notification.RecipientEmployeeIds.ShouldBe([Max]);
        notification.TemplateKey.ShouldBe("workforce.sick-note.store-manager");
        notification.Channels.ShouldBe([NotificationChannel.InApp]);
        notification.DeepLink.ShouldBe($"/absences/{absence.Id}");
    }

    [Fact]
    public async Task The_reason_travels_only_in_the_bc_sync_not_in_availability_or_notification_data()
    {
        await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        var availabilityFields = typeof(EmployeeAvailabilityChanged).GetProperties().Select(p => p.Name);
        availabilityFields.ShouldNotContain(nameof(SyncAbsenceToBc.Type));
        var notification = _messages.Of<SendNotification>().Single();
        notification.Data.Keys.Order().ShouldBe(["employeeName", "from", "to"]);
        notification.Data.Values.ShouldAllBe(v => !v.Contains("Krank", StringComparison.OrdinalIgnoreCase) && !v.Contains("Sickness", StringComparison.Ordinal));
        notification.Channels.ShouldBe([NotificationChannel.InApp]); // class 2 data only in-app
        _messages.Of<SyncAbsenceToBc>().Single().Type.ShouldBe(AbsenceType.Sickness);
    }

    [Fact]
    public async Task Sick_note_without_end_date_is_open_ended_and_blocks_later_absences_until_it_is_ended()
    {
        var sick = (await Service().ReportSickAsync(Erika, D("2026-07-15"), null, CancellationToken.None)).Absence!;
        _messages.Sent.Clear();

        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-11"), CancellationToken.None)).Error.ShouldBe(AbsenceError.Overlap);

        (await Service().EndSickNoteAsync(sick.Id, D("2026-07-24"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        sick.To.ShouldBe(D("2026-07-24"));
        sick.Version.ShouldBe(2);
        _messages.Of<SyncAbsenceToBc>().Single().Version.ShouldBe(2);
        _messages.Of<EmployeeAvailabilityChanged>().Single().To.ShouldBe(D("2026-07-24"));
        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-11"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Only_open_sick_notes_can_be_ended_and_not_before_their_start()
    {
        var closed = (await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-16"), CancellationToken.None)).Absence!;
        var open = (await Service().ReportSickAsync(Erika, D("2026-07-20"), null, CancellationToken.None)).Absence!;

        (await Service().EndSickNoteAsync(closed.Id, D("2026-07-18"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
        (await Service().EndSickNoteAsync(open.Id, D("2026-07-19"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidPeriod);
        (await Service().EndSickNoteAsync(Guid.NewGuid(), D("2026-07-30"), CancellationToken.None)).Error.ShouldBe(AbsenceError.NotFound);
    }

    [Fact]
    public async Task Overlapping_sick_notes_are_refused()
    {
        await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        (await Service().ReportSickAsync(Erika, D("2026-07-17"), D("2026-07-20"), CancellationToken.None)).Error.ShouldBe(AbsenceError.Overlap);
        (await Service().ReportSickAsync(Erika, D("2026-07-18"), D("2026-07-20"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Sick_note_period_may_not_end_before_it_starts() =>
        (await Service().ReportSickAsync(Erika, D("2026-07-17"), D("2026-07-15"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidPeriod);

    [Fact]
    public async Task Unknown_person_and_person_who_has_left_cannot_be_reported()
    {
        (await Service().ReportSickAsync(Guid.NewGuid(), D("2026-07-15"), null, CancellationToken.None)).Error.ShouldBe(AbsenceError.UnknownPerson);

        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-07-14") };
        (await Service().ReportSickAsync(Erika, D("2026-07-15"), null, CancellationToken.None)).Error.ShouldBe(AbsenceError.AfterExit);
    }

    [Fact]
    public async Task Sick_note_without_anybody_responsible_is_still_recorded_but_nobody_is_notified()
    {
        _responsibilities.Approvers.Clear();

        (await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-16"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        _messages.Of<SendNotification>().ShouldBeEmpty();
        _messages.Of<SyncAbsenceToBc>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Notification_ids_are_stable_so_a_repeated_command_is_idempotent_at_the_receiver()
    {
        var sick = (await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-16"), CancellationToken.None)).Absence!;
        var first = _messages.Of<SendNotification>().Single().NotificationId;

        first.ShouldBe(DeterministicGuid.From($"{sick.Id}:{sick.Version}:workforce.sick-note.store-manager"));
        first.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Sick_note_just_after_local_midnight_uses_the_local_date_not_the_utc_date()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.Zero)); // 00:30 on 29 March local: the day of the change to summer time

        var sick = (await Service().ReportSickAsync(Erika, Service().TodayLocal, null, CancellationToken.None)).Absence!;

        sick.From.ShouldBe(D("2026-03-29"));
    }

    // ---------------------------------------------------------------- vacation request

    [Fact]
    public async Task Vacation_request_creates_an_approval_by_the_responsible_person_and_notifies_them()
    {
        var result = await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None);

        var absence = result.Absence!;
        absence.Status.ShouldBe(AbsenceStatus.Requested);
        absence.Type.ShouldBe(AbsenceType.Vacation);
        var approval = _store.Approvals[absence.ApprovalRequestId!.Value];
        approval.RequesterId.ShouldBe(Erika);
        approval.CurrentApproverIds.ShouldBe([Max]);
        approval.SubjectId.ShouldBe(absence.Id);
        _messages.Of<SendNotification>().Single().TemplateKey.ShouldBe("workforce.vacation-request.supervisor");
        _messages.Of<SyncAbsenceToBc>().Single().Status.ShouldBe(AbsenceStatus.Requested);
        _messages.Of<EmployeeAvailabilityChanged>().ShouldBeEmpty(); // not yet approved
    }

    [Fact]
    public async Task All_responsible_persons_are_alternatives_of_the_one_approval_step()
    {
        _responsibilities.Approvers[Erika] = [Max, Tom];

        var absence = (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Absence!;

        _store.Approvals[absence.ApprovalRequestId!.Value].CurrentApproverIds.ShouldBe([Max, Tom], ignoreOrder: true);
        _messages.Of<SendNotification>().Single().RecipientEmployeeIds.ShouldBe([Max, Tom], ignoreOrder: true);
    }

    [Fact]
    public async Task Vacation_request_without_anybody_to_approve_is_refused()
    {
        _responsibilities.Approvers.Clear();

        (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Error.ShouldBe(AbsenceError.NoApprover);
        _store.Absences.ShouldBeEmpty();
        _messages.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_person_cannot_approve_their_own_request()
    {
        _responsibilities.Approvers[Erika] = [Erika];

        (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Error.ShouldBe(AbsenceError.NoApprover);
    }

    [Theory]
    [InlineData("2026-07-14", "2026-07-16")] // starts yesterday
    [InlineData("2026-08-08", "2026-08-03")] // ends before it starts
    [InlineData("2026-08-09", "2026-08-09")] // only a Sunday: no working day to book
    public async Task Invalid_vacation_periods_are_refused(string from, string to) =>
        (await Service().RequestVacationAsync(Erika, D(from), D(to), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidPeriod);

    [Fact]
    public async Task A_single_saturday_is_one_working_day_in_the_default_week() =>
        (await Service().RequestVacationAsync(Erika, D("2026-08-15"), D("2026-08-15"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

    [Fact]
    public async Task Vacation_starting_today_is_allowed_one_starting_yesterday_is_not()
    {
        (await Service().RequestVacationAsync(Erika, D("2026-07-15"), D("2026-07-15"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        (await Service().RequestVacationAsync(Erika, D("2026-07-14"), D("2026-07-14"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidPeriod);
    }

    [Fact]
    public async Task Request_beyond_the_remaining_days_is_a_business_error_and_stores_nothing()
    {
        _entitlements.Days[(Erika, 2026)] = 5;

        (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-10"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance); // 7 days

        _store.Absences.ShouldBeEmpty();
        _messages.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pending_requests_reserve_days_so_a_second_request_cannot_exceed_the_account()
    {
        _entitlements.Days[(Erika, 2026)] = 8;
        (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-07"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None); // 5

        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-10"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance); // 4 > 3 left
        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-09"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);            // 3 fits
    }

    [Fact]
    public async Task Missing_entitlement_for_a_year_is_reported_separately()
    {
        _entitlements.Days.Remove((Erika, 2026));

        (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-04"), CancellationToken.None)).Error.ShouldBe(AbsenceError.NoEntitlement);
    }

    [Fact]
    public async Task Vacation_across_the_year_end_is_checked_against_both_years_and_excludes_new_year()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        _entitlements.Days[(Erika, 2026)] = 4;
        _entitlements.Days[(Erika, 2027)] = 1;

        // 28 Dec .. 5 Jan: 4 days in 2026, 2 Jan (Sat), 4 and 5 Jan in 2027 = 3 days (1 Jan is a holiday)
        (await Service().RequestVacationAsync(Erika, D("2026-12-28"), D("2027-01-05"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance);

        _entitlements.Days[(Erika, 2027)] = 3;
        (await Service().RequestVacationAsync(Erika, D("2026-12-28"), D("2027-01-05"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Public_holidays_in_the_period_are_not_charged_and_state_holidays_depend_on_the_configured_state()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero));
        _entitlements.Days[(Erika, 2026)] = 5;

        // Mon 1 June .. Sat 6 June: 6 working days without a state, 5 in NW (Fronleichnam on Thursday 4 June)
        (await Service().RequestVacationAsync(Erika, D("2026-06-01"), D("2026-06-06"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance);
        (await Service("NW").RequestVacationAsync(Erika, D("2026-06-01"), D("2026-06-06"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Easter_holidays_inside_the_period_are_not_charged()
    {
        _entitlements.Days[(Erika, 2027)] = 3;
        _time = new FakeTimeProvider(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

        // 2027: Good Friday 26 Mar, Easter Monday 29 Mar. Thu 25 .. Tue 30 Mar = Thu, Sat, Tue = 3 days
        (await Service().RequestVacationAsync(Erika, D("2027-03-25"), D("2027-03-30"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Leap_day_is_a_normal_working_day_in_the_count()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2028, 1, 10, 9, 0, 0, TimeSpan.Zero));
        _entitlements.Days[(Erika, 2028)] = 3;

        (await Service().RequestVacationAsync(Erika, D("2028-02-28"), D("2028-03-01"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        (await Service().RequestVacationAsync(Erika, D("2028-03-06"), D("2028-03-06"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance);
    }

    [Fact]
    public async Task Vacation_overlapping_any_other_active_absence_is_refused_but_rejected_or_withdrawn_ones_do_not_block()
    {
        var first = (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-07"), CancellationToken.None)).Absence!;

        (await Service().RequestVacationAsync(Erika, D("2026-08-07"), D("2026-08-10"), CancellationToken.None)).Error.ShouldBe(AbsenceError.Overlap);

        await Service().DecideVacationAsync(first.Id, Max, approve: false, CancellationToken.None);
        (await Service().RequestVacationAsync(Erika, D("2026-08-07"), D("2026-08-10"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Request_on_the_exit_day_is_possible_if_it_ends_with_it_but_not_if_it_reaches_beyond()
    {
        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-07-31") };

        (await Service().RequestVacationAsync(Erika, D("2026-07-31"), D("2026-07-31"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        (await Service().RequestVacationAsync(Erika, D("2026-08-01"), D("2026-08-01"), CancellationToken.None)).Error.ShouldBe(AbsenceError.AfterExit);
        (await Service().RequestVacationAsync(Erika, D("2026-07-29"), D("2026-08-03"), CancellationToken.None)).Error.ShouldBe(AbsenceError.AfterExit);
    }

    // ---------------------------------------------------------------- decision

    [Fact]
    public async Task Approval_by_the_responsible_person_approves_syncs_and_informs_the_requester()
    {
        var absence = (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Absence!;
        _messages.Sent.Clear();

        (await Service().DecideVacationAsync(absence.Id, Max, approve: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        absence.Status.ShouldBe(AbsenceStatus.Approved);
        absence.Version.ShouldBe(2);
        _messages.Of<SyncAbsenceToBc>().Single().ShouldMatch(absence, AbsenceType.Vacation, AbsenceStatus.Approved, 2);
        _messages.Of<EmployeeAvailabilityChanged>().Single().Status.ShouldBe(AvailabilityStatus.Unavailable);
        var notification = _messages.Of<SendNotification>().Single();
        notification.RecipientEmployeeIds.ShouldBe([Erika]);
        notification.TemplateKey.ShouldBe("workforce.vacation-decision.requester");
        notification.Data["decision"].ShouldBe("genehmigt");
    }

    [Fact]
    public async Task Rejection_informs_the_requester_and_frees_the_days()
    {
        var absence = (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Absence!;
        _messages.Sent.Clear();

        await Service().DecideVacationAsync(absence.Id, Max, approve: false, CancellationToken.None);

        absence.Status.ShouldBe(AbsenceStatus.Rejected);
        _messages.Of<SendNotification>().Single().Data["decision"].ShouldBe("abgelehnt");
        (await Service().BalanceAsync(Erika, 2026, CancellationToken.None))!.Planned.ShouldBe(0);
    }

    [Fact]
    public async Task Only_a_person_in_the_chain_can_decide_and_only_once()
    {
        var absence = (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Absence!;

        (await Service().DecideVacationAsync(absence.Id, Tom, true, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotAllowed);
        (await Service().DecideVacationAsync(absence.Id, Erika, true, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotAllowed);
        (await Service().DecideVacationAsync(absence.Id, Max, true, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        (await Service().DecideVacationAsync(absence.Id, Max, false, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
        (await Service().DecideVacationAsync(Guid.NewGuid(), Max, true, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotFound);
    }

    [Fact]
    public async Task Sick_notes_cannot_be_decided()
    {
        var sick = (await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-16"), CancellationToken.None)).Absence!;

        (await Service().DecideVacationAsync(sick.Id, Max, true, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
    }

    // ---------------------------------------------------------------- withdraw

    [Fact]
    public async Task Withdrawal_after_approval_credits_the_days_and_makes_the_person_available_again()
    {
        var vacation = await ApprovedVacation("2026-08-03", "2026-08-08");

        (await Service().WithdrawVacationAsync(vacation.Id, Erika, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        vacation.Status.ShouldBe(AbsenceStatus.Withdrawn);
        vacation.Version.ShouldBe(3);
        _messages.Of<SyncAbsenceToBc>().Single().Status.ShouldBe(AbsenceStatus.Withdrawn);
        _messages.Of<EmployeeAvailabilityChanged>().Single().Status.ShouldBe(AvailabilityStatus.Available);
        (await Service().BalanceAsync(Erika, 2026, CancellationToken.None))!.Remaining.ShouldBe(30);
        _store.Approvals[vacation.ApprovalRequestId!.Value].Status.ShouldBe(ApprovalStatus.Approved); // history of the decision stays
    }

    [Fact]
    public async Task Pending_request_can_be_withdrawn_and_closes_its_approval()
    {
        var vacation = (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Absence!;

        (await Service().WithdrawVacationAsync(vacation.Id, Erika, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        _store.Approvals[vacation.ApprovalRequestId!.Value].Status.ShouldBe(ApprovalStatus.Withdrawn);
        (await Service().DecideVacationAsync(vacation.Id, Max, true, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
    }

    [Fact]
    public async Task Only_the_requester_can_withdraw_and_not_after_the_vacation_has_started()
    {
        var vacation = await ApprovedVacation("2026-07-15", "2026-07-18");

        (await Service().WithdrawVacationAsync(vacation.Id, Max, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotAllowed);
        (await Service().WithdrawVacationAsync(vacation.Id, Erika, CancellationToken.None)).Error.ShouldBe(AbsenceError.AlreadyStarted);
    }

    // ---------------------------------------------------------------- sickness during vacation

    [Fact]
    public async Task Sickness_in_approved_vacation_splits_it_credits_the_days_and_syncs_all_changed_absences()
    {
        var vacation = await ApprovedVacation("2026-08-03", "2026-08-15"); // Mon..Sat x2 = 12 days
        _entitlements.Days[(Erika, 2026)] = 30;

        var sick = (await Service().ReportSickAsync(Erika, D("2026-08-06"), D("2026-08-07"), CancellationToken.None)).Absence!;

        vacation.To.ShouldBe(D("2026-08-05"));
        vacation.Version.ShouldBe(3);
        var rest = _store.Absences.Single(a => a.Type == AbsenceType.Vacation && a.Id != vacation.Id);
        rest.Range.ShouldBe(new DateRange(D("2026-08-08"), D("2026-08-15")));
        rest.Status.ShouldBe(AbsenceStatus.Approved);
        rest.Version.ShouldBe(1);
        rest.ApprovalRequestId.ShouldBe(vacation.ApprovalRequestId);
        var balance = (await Service().BalanceAsync(Erika, 2026, CancellationToken.None))!;
        balance.Planned.ShouldBe(10); // 12 working days booked, the 2 sick days (Thu 6, Fri 7 Aug) are credited
        _messages.Of<SyncAbsenceToBc>().Select(s => s.AbsenceId).ShouldBe([sick.Id, vacation.Id, rest.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Sickness_covering_the_whole_approved_vacation_cancels_it()
    {
        var vacation = await ApprovedVacation("2026-08-03", "2026-08-05");

        await Service().ReportSickAsync(Erika, D("2026-08-01"), D("2026-08-10"), CancellationToken.None);

        vacation.Status.ShouldBe(AbsenceStatus.Cancelled);
        vacation.Version.ShouldBe(3);
        _store.Absences.Count(a => a.Type == AbsenceType.Vacation).ShouldBe(1);
        (await Service().BalanceAsync(Erika, 2026, CancellationToken.None))!.Planned.ShouldBe(0);
    }

    [Fact]
    public async Task Open_ended_sickness_cuts_off_the_rest_of_the_vacation()
    {
        var vacation = await ApprovedVacation("2026-08-03", "2026-08-15");

        await Service().ReportSickAsync(Erika, D("2026-08-06"), null, CancellationToken.None);

        vacation.To.ShouldBe(D("2026-08-05"));
        _store.Absences.Count(a => a.Type == AbsenceType.Vacation).ShouldBe(1);
    }

    [Fact]
    public async Task Sickness_overlapping_a_pending_vacation_cancels_the_request_and_its_approval()
    {
        var pending = (await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-08"), CancellationToken.None)).Absence!;

        await Service().ReportSickAsync(Erika, D("2026-08-05"), D("2026-08-06"), CancellationToken.None);

        pending.Status.ShouldBe(AbsenceStatus.Cancelled);
        _store.Approvals[pending.ApprovalRequestId!.Value].Status.ShouldBe(ApprovalStatus.Withdrawn);
    }

    // ---------------------------------------------------------------- exit

    [Fact]
    public async Task Exit_cancels_future_vacation_and_cuts_vacation_across_the_exit_date()
    {
        var after = (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-09"), CancellationToken.None)).Absence!;
        var across = await ApprovedVacation("2026-07-27", "2026-08-08");
        _messages.Sent.Clear();

        await Service().OnEmployeeLeftAsync(Erika, D("2026-07-31"), CancellationToken.None);

        after.Status.ShouldBe(AbsenceStatus.Cancelled);
        _store.Approvals[after.ApprovalRequestId!.Value].Status.ShouldBe(ApprovalStatus.Withdrawn);
        across.To.ShouldBe(D("2026-07-31"));
        across.Status.ShouldBe(AbsenceStatus.Approved);
        _messages.Of<SyncAbsenceToBc>().Count().ShouldBe(2);
        _messages.Of<EmployeeAvailabilityChanged>().Single().ShouldSatisfyAllConditions(
            m => m.Status.ShouldBe(AvailabilityStatus.Available), m => m.From.ShouldBe(D("2026-08-01")), m => m.To.ShouldBe(D("2026-08-08")));
    }

    [Fact]
    public async Task Exit_ends_an_open_ended_sick_note_on_the_exit_date_and_cancels_one_that_starts_later()
    {
        var running = (await Service().ReportSickAsync(Erika, D("2026-07-15"), null, CancellationToken.None)).Absence!;
        _messages.Sent.Clear();

        await Service().OnEmployeeLeftAsync(Erika, D("2026-07-31"), CancellationToken.None);

        running.To.ShouldBe(D("2026-07-31"));
        running.Status.ShouldBe(AbsenceStatus.Reported);
        running.Version.ShouldBe(2);
        _messages.Of<SyncAbsenceToBc>().Single().To.ShouldBe(D("2026-07-31"));

        var later = (await Service().ReportSickAsync(Erika, D("2026-08-10"), D("2026-08-12"), CancellationToken.None)).Absence!;
        await Service().OnEmployeeLeftAsync(Erika, D("2026-07-31"), CancellationToken.None);
        later.Status.ShouldBe(AbsenceStatus.Cancelled);
    }

    [Fact]
    public async Task Exit_leaves_absences_before_the_exit_untouched()
    {
        var before = await ApprovedVacation("2026-07-20", "2026-07-25");

        await Service().OnEmployeeLeftAsync(Erika, D("2026-07-31"), CancellationToken.None);

        before.Version.ShouldBe(2);
        _messages.Sent.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- account

    [Fact]
    public async Task Entitlement_is_maintained_per_year_and_the_balance_follows_the_bookings()
    {
        await Service().SetEntitlementAsync(Erika, 2026, 28.5m, CancellationToken.None);
        await Service().RequestVacationAsync(Erika, D("2026-08-03"), D("2026-08-07"), CancellationToken.None);

        (await Service().BalanceAsync(Erika, 2026, CancellationToken.None)).ShouldBe(new VacationBalance(2026, 28.5m, 0, 5, 23.5m));
        (await Service().BalanceAsync(Erika, 2030, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(367)]
    public async Task Entitlement_must_be_sensible(int days) =>
        (await Service().SetEntitlementAsync(Erika, 2026, days, CancellationToken.None)).ShouldBe(AbsenceError.InvalidPeriod);

    // ---------------------------------------------------------------- additional days and joining during the year

    private async Task<Guid> AddExtra(ExtraVacationKind kind, int year, decimal days)
    {
        var result = await Service().AddExtraVacationAsync(Erika, kind, year, days, Max, CancellationToken.None);
        result.Error.ShouldBe(AbsenceError.None);
        return result.Id!.Value;
    }

    [Fact]
    public async Task Additional_days_make_a_request_possible_that_the_contract_alone_would_not_cover()
    {
        _entitlements.Days[(Erika, 2026)] = 2;
        await AddExtra(ExtraVacationKind.Additional, 2026, 3);

        // Mon 7 Sep: 6 days (Mon-Sat) do not fit into 2 + 3, 4 days do.
        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-12"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance);
        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-10"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        (await Service().BalanceAsync(Erika, 2026, CancellationToken.None)).ShouldBe(new VacationBalance(2026, 5, 0, 4, 1));
    }

    [Fact]
    public async Task A_request_cannot_count_on_additional_days_that_expire_before_it()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero));
        _entitlements.Days[(Erika, 2026)] = 2;
        await Service().SetVacationRulesAsync(3, 31, Max, CancellationToken.None);
        await AddExtra(ExtraVacationKind.CarryOver, 2025, 2.5m); // usable until 31 March 2026

        (await Service().RequestVacationAsync(Erika, D("2026-04-13"), D("2026-04-15"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance); // 3 > 2
        (await Service().RequestVacationAsync(Erika, D("2026-02-09"), D("2026-02-11"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);              // 3 <= 4.5
    }

    [Fact]
    public async Task Leftover_days_that_have_expired_do_not_count_any_more()
    {
        // Today is 15 July 2026: the leftover of 2025 expired on 31 March.
        _entitlements.Days[(Erika, 2026)] = 2;
        await Service().SetVacationRulesAsync(3, 31, Max, CancellationToken.None);
        await AddExtra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-09"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance);
        var breakdown = (await Service().BreakdownAsync(Erika, 2026, CancellationToken.None))!;
        (breakdown.ExtraDays, breakdown.ForfeitedExtraDays, breakdown.EffectiveDays).ShouldBe((0m, 2.5m, 2m));
    }

    [Fact]
    public async Task Somebody_who_joins_during_the_year_gets_the_pro_rata_contractual_days()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        _people.People[Erika] = _people.People[Erika] with { EntryDate = D("2026-12-01") };
        _entitlements.Days[(Erika, 2026)] = 25;

        // 25 / 12 * 1 month = 2.083, rounded up: 2.5 days.
        var breakdown = (await Service().BreakdownAsync(Erika, 2026, CancellationToken.None))!;
        (breakdown.ContractualAnnualDays, breakdown.ContractualDays, breakdown.EffectiveDays).ShouldBe((25m, 2.5m, 2.5m));
        (await Service().RequestVacationAsync(Erika, D("2026-12-14"), D("2026-12-16"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InsufficientBalance); // 3 > 2.5
        (await Service().RequestVacationAsync(Erika, D("2026-12-14"), D("2026-12-15"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);              // 2 fit
    }

    [Fact]
    public async Task Without_a_rule_from_HR_additional_days_never_expire()
    {
        _entitlements.Days[(Erika, 2026)] = 2;
        await AddExtra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        var rules = await Service().GetVacationRulesAsync(CancellationToken.None);
        (rules.Rules, rules.IsDefault, rules.ChangedAt, rules.ChangedBy).ShouldBe((VacationRules.NeverExpire, true, null, null));
        // Today is 15 July 2026, long after March: the leftover still counts and a request may use it.
        (await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-09"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        (await Service().BreakdownAsync(Erika, 2026, CancellationToken.None))!.ForfeitedExtraDays.ShouldBe(0m);
    }

    [Fact]
    public async Task HR_sets_and_resets_the_expiry_rule()
    {
        (await Service().SetVacationRulesAsync(3, 31, Max, CancellationToken.None)).ShouldBe(AbsenceError.None);
        var set = await Service().GetVacationRulesAsync(CancellationToken.None);
        (set.Rules, set.IsDefault, set.ChangedBy, set.ChangedAt).ShouldBe((VacationRules.EndOfFirstQuarter, false, (Guid?)Max, (DateTimeOffset?)_time.GetUtcNow()));

        (await Service().SetVacationRulesAsync(null, null, Max, CancellationToken.None)).ShouldBe(AbsenceError.None);
        (await Service().GetVacationRulesAsync(CancellationToken.None)).Rules.ShouldBe(VacationRules.NeverExpire);
    }

    [Theory]
    [InlineData(3, null)]
    [InlineData(null, 31)]
    [InlineData(13, 1)]
    [InlineData(4, 31)]
    public async Task An_invalid_expiry_rule_is_refused_and_changes_nothing(int? month, int? day)
    {
        (await Service().SetVacationRulesAsync(month, day, Max, CancellationToken.None)).ShouldBe(AbsenceError.InvalidVacationRules);
        (await Service().GetVacationRulesAsync(CancellationToken.None)).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Additional_days_are_maintained_by_year_and_can_be_removed()
    {
        var id = await AddExtra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        var listed = await Service().ListExtraVacationAsync(Erika, CancellationToken.None);
        listed.ShouldHaveSingleItem().ShouldBe(new ExtraVacationDays(id, ExtraVacationKind.CarryOver, 2025, 2.5m));
        (await Service().RemoveExtraVacationAsync(Erika, id, CancellationToken.None)).ShouldBe(AbsenceError.None);
        (await Service().ListExtraVacationAsync(Erika, CancellationToken.None)).ShouldBeEmpty();
        (await Service().RemoveExtraVacationAsync(Erika, id, CancellationToken.None)).ShouldBe(AbsenceError.NotFound);
    }

    [Theory]
    [InlineData(ExtraVacationKind.CarryOver, 2025, 0)]
    [InlineData(ExtraVacationKind.CarryOver, 2025, -1)]
    [InlineData(ExtraVacationKind.CarryOver, 2025, 2.25)] // only whole and half days
    [InlineData(ExtraVacationKind.CarryOver, 2025, 101)]
    [InlineData(ExtraVacationKind.Unknown, 2025, 1)]
    [InlineData(ExtraVacationKind.Additional, 1999, 1)]
    public async Task Additional_days_must_be_sensible(ExtraVacationKind kind, int year, double days) =>
        (await Service().AddExtraVacationAsync(Erika, kind, year, (decimal)days, Max, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidPeriod);

    [Fact]
    public async Task Additional_days_of_an_unknown_person_are_refused() =>
        (await Service().AddExtraVacationAsync(Guid.NewGuid(), ExtraVacationKind.Additional, 2026, 1, Max, CancellationToken.None)).Error.ShouldBe(AbsenceError.UnknownPerson);
}

public class AbsenceSyncStatusTests
{
    private static Absence NewAbsence(long version) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Type = AbsenceType.Sickness, From = new DateOnly(2026, 7, 15), Status = AbsenceStatus.Reported,
        Version = version, SyncStatus = SyncStatus.Pending,
    };

    [Fact]
    public void Success_for_the_current_version_marks_the_absence_synced()
    {
        var absence = NewAbsence(2);

        absence.ApplySyncResult(2, succeeded: true).ShouldBeTrue();

        absence.SyncStatus.ShouldBe(SyncStatus.Synced);
        absence.SyncedVersion.ShouldBe(2);
    }

    [Fact]
    public void Failure_for_the_current_version_marks_it_failed_and_keeps_the_last_synced_version()
    {
        var absence = NewAbsence(3);
        absence.ApplySyncResult(2, true); // older success while version 3 is in flight

        absence.SyncStatus.ShouldBe(SyncStatus.Pending);
        absence.ApplySyncResult(3, succeeded: false).ShouldBeTrue();

        absence.SyncStatus.ShouldBe(SyncStatus.Failed);
        absence.SyncedVersion.ShouldBe(2);
    }

    [Fact]
    public void Results_for_outdated_versions_do_not_change_the_status()
    {
        var absence = NewAbsence(3);

        absence.ApplySyncResult(1, succeeded: false).ShouldBeFalse();

        absence.SyncStatus.ShouldBe(SyncStatus.Pending);
    }

    [Fact]
    public void A_new_change_resets_the_status_to_pending()
    {
        var absence = NewAbsence(1);
        absence.ApplySyncResult(1, true);

        absence.Touch(DateTimeOffset.UnixEpoch);

        absence.Version.ShouldBe(2);
        absence.SyncStatus.ShouldBe(SyncStatus.Pending);
    }
}

internal static class MessageAssertions
{
    public static void ShouldMatch(this SyncAbsenceToBc message, Absence absence, AbsenceType type, AbsenceStatus status, long version)
    {
        message.AbsenceId.ShouldBe(absence.Id);
        message.EmployeeId.ShouldBe(absence.EmployeeId);
        message.Type.ShouldBe(type);
        message.Status.ShouldBe(status);
        message.Version.ShouldBe(version);
        message.From.ShouldBe(absence.From);
        message.To.ShouldBe(absence.To);
    }
}
