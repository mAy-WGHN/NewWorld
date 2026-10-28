using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Unavailability;

namespace Workforce.Core.Tests;

/// <summary>How the absences (sickness, vacation) work together with the unavailability list: KU, vacation against entries, uncovered shifts.</summary>
public class UnavailabilityAbsenceTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid HrAnna = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-000000000017");

    private readonly FakeStore _store = new();
    private readonly FakePeople _people = new();
    private readonly FakeEntitlements _entitlements = new();
    private readonly FakeExtraVacation _extraVacation = new();
    private readonly FakeResponsibilities _responsibilities = new();
    private readonly FakeUnavailabilityStore _unavailability = new();
    private readonly FakeShifts _shifts = new();
    private readonly RecordingMessages _messages = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero));

    public UnavailabilityAbsenceTests()
    {
        _people.People[Erika] = new PersonInfo(Erika, "Erika Mustermann", Store, null);
        _entitlements.Days[(Erika, 2026)] = 30;
        _responsibilities.Approvers[Erika] = [Max];
        _responsibilities.ScheduleAdjustmentHandlers[Erika] = [HrAnna];
    }

    private AbsenceService Service() => new(
        _store, _people, _entitlements, _extraVacation, _responsibilities, _messages, _time,
        new WorkforceOptions(), new GermanHolidayCalendar(),
        _unavailability, new ShiftCoverage(_shifts, _responsibilities, _messages, _time, new WorkforceOptions()));

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private async Task<Absence> ApprovedVacation(string from, string to)
    {
        var requested = await Service().RequestVacationAsync(Erika, D(from), D(to), CancellationToken.None);
        (await Service().DecideVacationAsync(requested.Absence!.Id, Max, approve: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        _messages.Sent.Clear();
        return requested.Absence;
    }

    private Guid KuId => _unavailability.Kinds.Single(k => k.Code == UnavailabilityKinds.SickInVacation).Id;

    [Fact]
    public async Task Sickness_inside_approved_vacation_is_recorded_as_KU_for_exactly_those_days()
    {
        var vacation = await ApprovedVacation("2026-08-03", "2026-08-14");

        var sick = (await Service().ReportSickAsync(Erika, D("2026-08-05"), D("2026-08-07"), CancellationToken.None)).Absence!;

        var ku = _unavailability.Entries.Single();
        (ku.KindId, ku.From, ku.To, ku.Source, ku.SourceAbsenceId, ku.EmployeeId).ShouldBe((KuId, D("2026-08-05"), D("2026-08-07"), EntrySource.System, sick.Id, Erika));
        vacation.To.ShouldBe(D("2026-08-04")); // the vacation is still cut and credited as before
    }

    [Fact]
    public async Task Sickness_reaching_beyond_the_vacation_gets_KU_only_for_the_overlap()
    {
        await ApprovedVacation("2026-08-03", "2026-08-07");

        await Service().ReportSickAsync(Erika, D("2026-08-05"), D("2026-08-12"), CancellationToken.None);

        var ku = _unavailability.Entries.Single();
        (ku.From, ku.To).ShouldBe((D("2026-08-05"), D("2026-08-07")));
    }

    [Fact]
    public async Task Open_ended_sickness_starting_inside_the_vacation_gets_KU_until_the_vacation_ends()
    {
        await ApprovedVacation("2026-08-03", "2026-08-14");

        await Service().ReportSickAsync(Erika, D("2026-08-10"), null, CancellationToken.None);

        var ku = _unavailability.Entries.Single();
        (ku.From, ku.To).ShouldBe((D("2026-08-10"), D("2026-08-14")));
    }

    [Fact]
    public async Task Sickness_without_vacation_or_during_a_mere_request_creates_no_KU()
    {
        await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);
        await Service().RequestVacationAsync(Erika, D("2026-09-07"), D("2026-09-11"), CancellationToken.None);
        await Service().ReportSickAsync(Erika, D("2026-09-08"), D("2026-09-09"), CancellationToken.None);

        _unavailability.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Extending_a_sick_note_into_vacation_adds_KU_for_the_newly_covered_days()
    {
        await ApprovedVacation("2026-08-03", "2026-08-14");
        var sick = (await Service().ReportSickAsync(Erika, D("2026-07-30"), D("2026-08-04"), CancellationToken.None)).Absence!;
        _unavailability.Entries.Single().To.ShouldBe(D("2026-08-04"));

        (await Service().ChangeSickNotePeriodAsync(sick, D("2026-07-30"), D("2026-08-06"), CancellationToken.None)).ShouldBe(AbsenceError.None);

        _unavailability.Entries.Select(e => (e.From, e.To)).ShouldBe([(D("2026-08-03"), D("2026-08-04")), (D("2026-08-05"), D("2026-08-06"))]);
    }

    [Fact]
    public async Task A_vacation_request_may_not_overlap_a_manual_entry()
    {
        _unavailability.Entries.Add(new UnavailabilityEntry
        {
            Id = Guid.NewGuid(), EmployeeId = Erika, KindId = _unavailability.Kinds.Single(k => k.Code == "ELTERNZEIT").Id, From = D("2026-09-07"), To = D("2026-09-09"), Source = EntrySource.Manual,
        });

        (await Service().RequestVacationAsync(Erika, D("2026-09-09"), D("2026-09-11"), CancellationToken.None)).Error.ShouldBe(AbsenceError.Overlap);
        (await Service().RequestVacationAsync(Erika, D("2026-09-10"), D("2026-09-11"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Reporting_sick_is_not_blocked_by_a_manual_entry_of_HR()
    {
        _unavailability.Entries.Add(new UnavailabilityEntry
        {
            Id = Guid.NewGuid(), EmployeeId = Erika, KindId = _unavailability.Kinds.Single(k => k.Code == "FREI").Id, From = D("2026-07-15"), To = D("2026-07-16"), Source = EntrySource.Manual,
        });

        (await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task A_sick_note_over_a_planned_shift_asks_HR_to_adjust_the_schedule_with_the_sickness_text()
    {
        var shift = new PlannedShift(Guid.NewGuid(), Store, "0010 BaBu Oberhausen", new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 7, 16, 17, 0, 0, TimeSpan.FromHours(2)));
        _shifts.Planned.Add((Erika, shift));

        await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        _messages.Of<ShiftUncovered>().Single().ShiftId.ShouldBe(shift.ShiftId);
        var notification = _messages.Of<SendNotification>().Single(n => n.TemplateKey.StartsWith("workforce.shift-uncovered", StringComparison.Ordinal));
        notification.TemplateKey.ShouldBe("workforce.shift-uncovered-sickness.hr");
        notification.RecipientEmployeeIds.ShouldBe([HrAnna]);
        notification.Data["DP.month"].ShouldBe("Juli");
    }

    [Fact]
    public async Task Without_planned_shifts_a_sick_note_sends_the_same_messages_as_before()
    {
        await Service().ReportSickAsync(Erika, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        _messages.Sent.Count.ShouldBe(3);
        _messages.Of<ShiftUncovered>().ShouldBeEmpty();
    }
}
