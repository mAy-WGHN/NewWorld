using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Unavailability;

namespace Workforce.Core.Tests;

public class UnavailabilityServiceTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid HrAnna = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid HrBen = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-000000000017");

    private readonly FakeUnavailabilityStore _store = new();
    private readonly FakeStore _absences = new();
    private readonly FakePeople _people = new();
    private readonly FakeResponsibilities _responsibilities = new();
    private readonly FakeShifts _shifts = new();
    private readonly RecordingMessages _messages = new();

    // Wednesday 2026-07-15, 10:00 local (summer time)
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero));

    public UnavailabilityServiceTests()
    {
        _people.People[Erika] = new PersonInfo(Erika, "Erika Mustermann", Store, null, EntryDate: new DateOnly(2020, 3, 1));
        _responsibilities.ScheduleAdjustmentHandlers[Erika] = [HrAnna, HrBen];
    }

    private UnavailabilityService Service() => new(
        _store, _absences, _people, _messages, new ShiftCoverage(_shifts, _responsibilities, _messages, _time, new WorkforceOptions()), _time);

    private Guid KindId(string code) => _store.Kinds.Single(k => k.Code == code).Id;

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private static PlannedShift Shift(DateTimeOffset from, string label = "0010 BaBu Oberhausen") =>
        new(Guid.NewGuid(), Store, label, from, from.AddHours(8));

    // ---------------------------------------------------------------- the kinds

    [Fact]
    public void The_seed_has_the_24_kinds_of_the_BC_list_with_unique_codes()
    {
        UnavailabilityKindSeed.All.Count.ShouldBe(24);
        UnavailabilityKindSeed.All.Select(k => k.Code.ToUpperInvariant()).Distinct().Count().ShouldBe(24);
        UnavailabilityKindSeed.All.Where(k => k.IsSystem).Select(k => k.Code).ShouldBe(["KRANK", "KRANK I URLAUB", "URLAUB"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("KRANK", "K", true)]
    [InlineData("URLAUB", "U", true)]
    [InlineData("KRANK I URLAUB", "KU", true)]
    [InlineData("ELTERNZEIT", "EZ", false)]
    [InlineData("MUTTERSCHUTZ", "MS", false)]
    [InlineData("UNBEZ. U", "UBE", false)]
    [InlineData("UB", "UB", true)]
    [InlineData("TOD", "Tod", true)]
    [InlineData("KLASSENFAHRT", null, false)]
    public void Kinds_carry_symbol_and_paid_time_as_in_BC(string code, string? symbol, bool paid)
    {
        var kind = _store.Kinds.Single(k => k.Code == code);

        (kind.ScheduleSymbol, kind.PaidTime, kind.IsActive).ShouldBe((symbol, paid, true));
    }

    [Fact]
    public async Task A_new_kind_needs_a_unique_code_ignoring_case()
    {
        var created = await Service().AddKindAsync("Wehrübung", "Wehrübung", paidTime: false, "WÜ", null, CancellationToken.None);
        created.Error.ShouldBe(UnavailabilityError.None);
        created.Kind!.IsSystem.ShouldBeFalse();

        (await Service().AddKindAsync("wehrübung", "doppelt", false, null, null, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.DuplicateCode);
        (await Service().AddKindAsync("  ", "leer", false, null, null, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.InvalidKind);
        (await Service().AddKindAsync("X", "", false, null, null, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.InvalidKind);
    }

    [Fact]
    public async Task A_kind_is_deactivated_not_deleted_and_the_system_kinds_stay_active()
    {
        (await Service().SetKindActiveAsync(KindId("FORTBILDUNG"), false, CancellationToken.None)).ShouldBe(UnavailabilityError.None);
        _store.Kinds.Single(k => k.Code == "FORTBILDUNG").IsActive.ShouldBeFalse();
        (await Service().KindsAsync(includeInactive: false, CancellationToken.None)).ShouldNotContain(k => k.Code == "FORTBILDUNG");
        (await Service().KindsAsync(includeInactive: true, CancellationToken.None)).ShouldContain(k => k.Code == "FORTBILDUNG");

        (await Service().SetKindActiveAsync(KindId("KRANK"), false, CancellationToken.None)).ShouldBe(UnavailabilityError.SystemKind);
        (await Service().SetKindActiveAsync(Guid.NewGuid(), false, CancellationToken.None)).ShouldBe(UnavailabilityError.UnknownKind);
    }

    [Fact]
    public async Task A_kind_can_be_changed_except_its_code()
    {
        (await Service().UpdateKindAsync(KindId("REHA"), "Rehabilitation", paidTime: true, "RH", "WC1", CancellationToken.None)).ShouldBe(UnavailabilityError.None);

        var kind = _store.Kinds.Single(k => k.Code == "REHA");
        (kind.Description, kind.PaidTime, kind.ScheduleSymbol, kind.WorkCode).ShouldBe(("Rehabilitation", true, "RH", "WC1"));
    }

    // ---------------------------------------------------------------- entries

    [Fact]
    public async Task HR_enters_a_period_of_a_kind_and_only_the_neutral_availability_leaves_the_service()
    {
        var result = await Service().AddEntryAsync(Erika, KindId("SONDERURLAUB"), D("2026-09-01"), D("2026-09-02"), HrAnna, CancellationToken.None);

        result.Error.ShouldBe(UnavailabilityError.None);
        var entry = result.Entry!;
        (entry.EmployeeId, entry.From, entry.To, entry.Source, entry.CreatedBy, entry.CreatedAt).ShouldBe((Erika, D("2026-09-01"), D("2026-09-02"), EntrySource.Manual, HrAnna, _time.GetUtcNow()));
        _store.Entries.ShouldContain(entry);
        var availability = _messages.Of<EmployeeAvailabilityChanged>().Single();
        (availability.EmployeeId, availability.From, availability.To, availability.Status).ShouldBe((Erika, D("2026-09-01"), D("2026-09-02"), AvailabilityStatus.Unavailable));
        _messages.Of<SyncAbsenceToBc>().ShouldBeEmpty(); // TODO(BC-Modell): the mirror to BC is not defined yet
        _messages.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_single_day_is_a_valid_period()
    {
        (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-09-01"), D("2026-09-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.None);
    }

    [Fact]
    public async Task The_period_may_span_the_year_end_and_a_leap_day()
    {
        (await Service().AddEntryAsync(Erika, KindId("ELTERNZEIT"), D("2027-12-20"), D("2028-03-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.None);
    }

    [Fact]
    public async Task Invalid_entries_are_refused_and_change_nothing()
    {
        var service = Service();
        (await service.AddEntryAsync(Guid.NewGuid(), KindId("FREI"), D("2026-09-01"), D("2026-09-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.UnknownPerson);
        (await service.AddEntryAsync(Erika, Guid.NewGuid(), D("2026-09-01"), D("2026-09-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.UnknownKind);
        (await service.AddEntryAsync(Erika, KindId("FREI"), D("2026-09-02"), D("2026-09-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.InvalidPeriod);
        (await service.AddEntryAsync(Erika, KindId("FREI"), D("2020-02-28"), D("2020-03-02"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.BeforeEntry);

        _store.Entries.ShouldBeEmpty();
        _messages.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_deactivated_kind_cannot_be_entered()
    {
        await Service().SetKindActiveAsync(KindId("FORTBILDUNG"), false, CancellationToken.None);

        (await Service().AddEntryAsync(Erika, KindId("FORTBILDUNG"), D("2026-09-01"), D("2026-09-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.KindInactive);
    }

    [Theory]
    [InlineData("KRANK")]
    [InlineData("URLAUB")]
    [InlineData("KRANK I URLAUB")]
    public async Task Sickness_vacation_and_KU_come_from_the_absences_not_from_manual_entries(string code)
    {
        (await Service().AddEntryAsync(Erika, KindId(code), D("2026-09-01"), D("2026-09-02"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.KindNotEnterable);
    }

    [Fact]
    public async Task Nothing_can_be_entered_after_the_exit_date()
    {
        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-09-30") };

        (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-09-30"), D("2026-10-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.AfterExit);
        (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-09-29"), D("2026-09-30"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.None);
    }

    [Fact]
    public async Task Overlapping_entries_are_refused_also_by_one_day()
    {
        await Service().AddEntryAsync(Erika, KindId("ELTERNZEIT"), D("2026-09-01"), D("2026-09-10"), HrAnna, CancellationToken.None);

        (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-09-10"), D("2026-09-12"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.Overlap);
        (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-09-11"), D("2026-09-12"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.None);
    }

    [Theory]
    [InlineData(AbsenceType.Vacation, AbsenceStatus.Approved, true)]
    [InlineData(AbsenceType.Vacation, AbsenceStatus.Requested, true)]
    [InlineData(AbsenceType.Sickness, AbsenceStatus.Reported, true)]
    [InlineData(AbsenceType.Vacation, AbsenceStatus.Rejected, false)]
    [InlineData(AbsenceType.Vacation, AbsenceStatus.Withdrawn, false)]
    [InlineData(AbsenceType.Vacation, AbsenceStatus.Cancelled, false)]
    public async Task Entries_must_not_overlap_active_absences(AbsenceType type, AbsenceStatus status, bool refused)
    {
        _absences.Absences.Add(new Absence { Id = Guid.NewGuid(), EmployeeId = Erika, Type = type, From = D("2026-09-01"), To = D("2026-09-05"), Status = status });

        var result = await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-09-05"), D("2026-09-06"), HrAnna, CancellationToken.None);

        result.Error.ShouldBe(refused ? UnavailabilityError.Overlap : UnavailabilityError.None);
    }

    [Fact]
    public async Task An_open_ended_sick_note_blocks_every_later_entry()
    {
        _absences.Absences.Add(new Absence { Id = Guid.NewGuid(), EmployeeId = Erika, Type = AbsenceType.Sickness, From = D("2026-09-01"), To = null, Status = AbsenceStatus.Reported });

        (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2030-01-01"), D("2030-01-01"), HrAnna, CancellationToken.None)).Error.ShouldBe(UnavailabilityError.Overlap);
    }

    [Fact]
    public async Task A_manual_entry_can_be_removed_and_makes_the_person_available_again()
    {
        var entry = (await Service().AddEntryAsync(Erika, KindId("SONDERURLAUB"), D("2026-09-01"), D("2026-09-02"), HrAnna, CancellationToken.None)).Entry!;
        _messages.Sent.Clear();

        (await Service().RemoveEntryAsync(Erika, entry.Id, CancellationToken.None)).ShouldBe(UnavailabilityError.None);

        _store.Entries.ShouldBeEmpty();
        var availability = _messages.Of<EmployeeAvailabilityChanged>().Single();
        (availability.From, availability.To, availability.Status).ShouldBe((D("2026-09-01"), (DateOnly?)D("2026-09-02"), AvailabilityStatus.Available));
    }

    [Fact]
    public async Task Entries_of_other_persons_and_system_entries_cannot_be_removed()
    {
        var other = Guid.NewGuid();
        _people.People[other] = new PersonInfo(other, "Max", Store, null);
        var entry = (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-09-01"), D("2026-09-01"), HrAnna, CancellationToken.None)).Entry!;
        var system = new UnavailabilityEntry
        {
            Id = Guid.NewGuid(), EmployeeId = Erika, KindId = KindId("KRANK I URLAUB"), From = D("2026-08-05"), To = D("2026-08-06"), Source = EntrySource.System, SourceAbsenceId = Guid.NewGuid(),
        };
        _store.Entries.Add(system);

        (await Service().RemoveEntryAsync(other, entry.Id, CancellationToken.None)).ShouldBe(UnavailabilityError.NotFound);
        (await Service().RemoveEntryAsync(Erika, system.Id, CancellationToken.None)).ShouldBe(UnavailabilityError.SystemEntry);
        (await Service().RemoveEntryAsync(Erika, Guid.NewGuid(), CancellationToken.None)).ShouldBe(UnavailabilityError.NotFound);
    }

    [Fact]
    public async Task The_timeline_of_a_person_joins_absences_and_entries()
    {
        _absences.Absences.Add(new Absence { Id = Guid.NewGuid(), EmployeeId = Erika, Type = AbsenceType.Vacation, From = D("2026-10-01"), To = D("2026-10-05"), Status = AbsenceStatus.Approved });
        await Service().AddEntryAsync(Erika, KindId("SONDERURLAUB"), D("2026-09-01"), D("2026-09-02"), HrAnna, CancellationToken.None);

        (await Service().TimelineAsync(Erika, CancellationToken.None)).Select(i => i.KindCode).ShouldBe(["SONDERURLAUB", "URLAUB"]);
    }

    // ---------------------------------------------------------------- periods after the exit are only marked

    [Fact]
    public async Task Periods_that_reach_past_the_exit_date_are_marked_for_HR_but_nothing_is_changed()
    {
        await Service().AddEntryAsync(Erika, KindId("SONDERURLAUB"), D("2026-09-01"), D("2026-09-10"), HrAnna, CancellationToken.None);
        await Service().AddEntryAsync(Erika, KindId("FORTBILDUNG"), D("2026-09-15"), D("2026-09-16"), HrAnna, CancellationToken.None);
        await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-08-01"), D("2026-08-02"), HrAnna, CancellationToken.None);
        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-09-05") }; // the exit is reported later

        var items = await Service().TimelineAsync(Erika, CancellationToken.None);

        items.Select(i => (i.KindCode, i.AfterExit)).ShouldBe([("FREI", false), ("SONDERURLAUB", true), ("FORTBILDUNG", true)]);
        _store.Entries.Count.ShouldBe(3); // all still there, unchanged
        _store.Entries.Select(e => e.To).ShouldContain(D("2026-09-10"));
    }

    [Fact]
    public async Task A_period_ending_on_the_exit_date_is_not_marked_and_without_an_exit_nothing_is()
    {
        await Service().AddEntryAsync(Erika, KindId("SONDERURLAUB"), D("2026-09-01"), D("2026-09-05"), HrAnna, CancellationToken.None);

        (await Service().TimelineAsync(Erika, CancellationToken.None)).Single().AfterExit.ShouldBeFalse(); // no exit date

        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-09-05") };

        (await Service().TimelineAsync(Erika, CancellationToken.None)).Single().AfterExit.ShouldBeFalse(); // ends on the last day
    }

    [Fact]
    public async Task An_open_sick_note_after_a_reported_exit_is_marked()
    {
        _absences.Absences.Add(new Absence { Id = Guid.NewGuid(), EmployeeId = Erika, Type = AbsenceType.Sickness, From = D("2026-07-10"), To = null, Status = AbsenceStatus.Reported });
        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-07-31") };

        (await Service().TimelineAsync(Erika, CancellationToken.None)).Single().AfterExit.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- shifts without cover

    [Fact]
    public async Task A_shift_on_an_unavailable_day_is_reported_uncovered_and_HR_is_notified_once_per_store_and_month()
    {
        var shifts = new[]
        {
            Shift(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(1))),
            Shift(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromHours(1))),
        };
        _shifts.Planned.AddRange(shifts.Select(s => (Erika, s)));

        await Service().AddEntryAsync(Erika, KindId("SONDERURLAUB"), D("2026-11-03"), D("2026-11-04"), HrAnna, CancellationToken.None);

        _messages.Of<ShiftUncovered>().Select(u => (u.ShiftId, u.StoreId, u.From, u.To)).ShouldBe(shifts.Select(s => (s.ShiftId, s.StoreId, s.From, s.To)), ignoreOrder: true);
        var notification = _messages.Of<SendNotification>().Single();
        notification.TemplateKey.ShouldBe("workforce.shift-uncovered-absence.hr");
        notification.RecipientEmployeeIds.ShouldBe([HrAnna, HrBen], ignoreOrder: true);
        notification.Channels.ShouldBe([NotificationChannel.InApp]);
        notification.Data.ShouldBe(new Dictionary<string, string> { ["DP.store"] = "0010 BaBu Oberhausen", ["DP.month"] = "November", ["DP.year"] = "2026" });
    }

    [Fact]
    public async Task The_notification_carries_neither_the_person_nor_the_kind()
    {
        _shifts.Planned.Add((Erika, Shift(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(1)))));

        await Service().AddEntryAsync(Erika, KindId("ELTERNZEIT"), D("2026-11-03"), D("2026-11-03"), HrAnna, CancellationToken.None);

        var notification = _messages.Of<SendNotification>().Single();
        string.Join(' ', notification.Data.Values).ShouldNotContain("Erika");
        string.Join(' ', notification.Data.Values).ShouldNotContain("ELTERNZEIT", Case.Insensitive);
        _messages.Of<ShiftUncovered>().Single().ShouldNotBeNull();
    }

    [Fact]
    public async Task Shifts_in_different_months_or_stores_lead_to_separate_notifications_with_the_local_month()
    {
        var other = Guid.Parse("00000000-0000-0000-0000-000000000018");
        _shifts.Planned.AddRange(
        [
            // 2026-10-31 23:30 UTC is 2026-11-01 00:30 in Berlin: November
            (Erika, Shift(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero))),
            (Erika, Shift(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(1)))),
            (Erika, new PlannedShift(Guid.NewGuid(), other, "0020 Other", new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(1)), new DateTimeOffset(2026, 10, 30, 17, 0, 0, TimeSpan.FromHours(1)))),
        ]);

        await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-10-30"), D("2026-11-01"), HrAnna, CancellationToken.None);

        _messages.Of<SendNotification>().Select(n => (n.Data["DP.store"], n.Data["DP.month"], n.Data["DP.year"])).ShouldBe(
            [("0010 BaBu Oberhausen", "Oktober", "2026"), ("0020 Other", "Oktober", "2026"), ("0010 BaBu Oberhausen", "November", "2026")], ignoreOrder: true);
    }

    [Fact]
    public async Task The_year_end_separates_December_and_January()
    {
        _shifts.Planned.AddRange(
        [
            (Erika, Shift(new DateTimeOffset(2026, 12, 31, 9, 0, 0, TimeSpan.FromHours(1)))),
            (Erika, Shift(new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.FromHours(1)))),
        ]);

        await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-12-31"), D("2027-01-02"), HrAnna, CancellationToken.None);

        _messages.Of<SendNotification>().Select(n => (n.Data["DP.month"], n.Data["DP.year"])).ShouldBe([("Dezember", "2026"), ("Januar", "2027")], ignoreOrder: true);
    }

    [Fact]
    public async Task Nothing_happens_without_a_shift()
    {
        await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-11-03"), D("2026-11-03"), HrAnna, CancellationToken.None);

        _messages.Of<ShiftUncovered>().ShouldBeEmpty();
        _messages.Of<SendNotification>().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_notification_is_recognised_when_repeated_and_never_goes_to_the_person_herself()
    {
        _responsibilities.ScheduleAdjustmentHandlers[Erika] = [HrAnna, Erika];
        _shifts.Planned.Add((Erika, Shift(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(1)))));

        var entry = (await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-11-03"), D("2026-11-03"), HrAnna, CancellationToken.None)).Entry!;

        var first = _messages.Of<SendNotification>().Single();
        first.RecipientEmployeeIds.ShouldBe([HrAnna]);
        await new ShiftCoverage(_shifts, _responsibilities, _messages, _time, new WorkforceOptions())
            .ReportAsync(Erika, new DateRange(entry.From, entry.To), UncoveredReason.Absence, entry.Id, Guid.NewGuid(), CancellationToken.None);
        _messages.Of<SendNotification>().Select(n => n.NotificationId).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Without_HR_recipients_the_shifts_are_still_reported_but_nobody_is_notified()
    {
        _responsibilities.ScheduleAdjustmentHandlers.Clear();
        _shifts.Planned.Add((Erika, Shift(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(1)))));

        await Service().AddEntryAsync(Erika, KindId("FREI"), D("2026-11-03"), D("2026-11-03"), HrAnna, CancellationToken.None);

        _messages.Of<ShiftUncovered>().Count().ShouldBe(1);
        _messages.Of<SendNotification>().ShouldBeEmpty();
    }
}
