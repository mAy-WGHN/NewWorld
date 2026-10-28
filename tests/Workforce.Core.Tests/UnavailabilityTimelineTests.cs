using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Unavailability;

namespace Workforce.Core.Tests;

public class UnavailabilityTimelineTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly IReadOnlyList<UnavailabilityKind> Kinds = [.. UnavailabilityKindSeed.All.Select(s => s.ToKind())];

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private static UnavailabilityKind Kind(string code) => Kinds.Single(k => k.Code == code);

    private static Absence Sick(string from, string? to, AbsenceStatus status = AbsenceStatus.Reported) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Erika, Type = AbsenceType.Sickness, From = D(from), To = to is null ? null : D(to), Status = status,
    };

    private static Absence Vacation(string from, string to, AbsenceStatus status = AbsenceStatus.Approved) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Erika, Type = AbsenceType.Vacation, From = D(from), To = D(to), Status = status,
    };

    private static UnavailabilityEntry Entry(string code, string from, string to, EntrySource source = EntrySource.Manual, Guid? sourceAbsence = null) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Erika, KindId = Kind(code).Id, From = D(from), To = D(to), Source = source, SourceAbsenceId = sourceAbsence,
    };

    [Fact]
    public void Sickness_becomes_KRANK_and_approved_vacation_URLAUB()
    {
        var items = UnavailabilityTimeline.Build(Erika, [Sick("2026-07-15", "2026-07-17"), Vacation("2026-08-03", "2026-08-14")], [], Kinds);

        items.Select(i => (i.KindCode, i.From, i.To)).ShouldBe([("KRANK", D("2026-07-15"), (DateOnly?)D("2026-07-17")), ("URLAUB", D("2026-08-03"), D("2026-08-14"))]);
        items[0].Origin.ShouldBe(UnavailabilityOrigin.Absence);
        items[0].ScheduleSymbol.ShouldBe("K");
        items[0].PaidTime.ShouldBeTrue();
    }

    [Theory]
    [InlineData(AbsenceStatus.Requested)]
    [InlineData(AbsenceStatus.Rejected)]
    [InlineData(AbsenceStatus.Withdrawn)]
    [InlineData(AbsenceStatus.Cancelled)]
    public void Vacation_that_is_not_approved_is_not_unavailability(AbsenceStatus status)
    {
        UnavailabilityTimeline.Build(Erika, [Vacation("2026-08-03", "2026-08-14", status)], [], Kinds).ShouldBeEmpty();
    }

    [Fact]
    public void Open_ended_sickness_stays_open()
    {
        var item = UnavailabilityTimeline.Build(Erika, [Sick("2026-07-15", null)], [], Kinds).Single();

        item.To.ShouldBeNull();
    }

    [Fact]
    public void Manual_entries_carry_their_kind()
    {
        var items = UnavailabilityTimeline.Build(Erika, [], [Entry("SONDERURLAUB", "2026-09-01", "2026-09-02")], Kinds);

        var item = items.Single();
        (item.KindCode, item.KindDescription, item.Origin, item.ScheduleSymbol).ShouldBe(("SONDERURLAUB", "Sonder-Urlaub", UnavailabilityOrigin.Manual, "SU"));
    }

    [Fact]
    public void Sick_days_within_vacation_show_as_KU_and_split_the_sickness()
    {
        var sick = Sick("2026-08-05", "2026-08-12");
        var ku = Entry("KRANK I URLAUB", "2026-08-05", "2026-08-10", EntrySource.System, sick.Id);

        var items = UnavailabilityTimeline.Build(Erika, [sick], [ku], Kinds);

        items.Select(i => (i.KindCode, i.From, i.To)).ShouldBe([("KRANK I URLAUB", D("2026-08-05"), (DateOnly?)D("2026-08-10")), ("KRANK", D("2026-08-11"), D("2026-08-12"))]);
    }

    [Fact]
    public void KU_in_the_middle_splits_the_sickness_into_two_parts()
    {
        var sick = Sick("2026-08-01", "2026-08-20");
        var ku = Entry("KRANK I URLAUB", "2026-08-05", "2026-08-10", EntrySource.System, sick.Id);

        var items = UnavailabilityTimeline.Build(Erika, [sick], [ku], Kinds);

        items.Select(i => (i.KindCode, i.From, i.To)).ShouldBe([
            ("KRANK", D("2026-08-01"), (DateOnly?)D("2026-08-04")),
            ("KRANK I URLAUB", D("2026-08-05"), D("2026-08-10")),
            ("KRANK", D("2026-08-11"), D("2026-08-20"))]);
    }

    [Fact]
    public void KU_is_clipped_to_the_sick_note_when_the_sickness_was_shortened()
    {
        var sick = Sick("2026-08-05", "2026-08-07");
        var ku = Entry("KRANK I URLAUB", "2026-08-05", "2026-08-10", EntrySource.System, sick.Id);

        var items = UnavailabilityTimeline.Build(Erika, [sick], [ku], Kinds);

        items.Single().ShouldMatch("KRANK I URLAUB", D("2026-08-05"), D("2026-08-07"));
    }

    [Fact]
    public void KU_without_its_sick_note_is_dropped()
    {
        UnavailabilityTimeline.Build(Erika, [], [Entry("KRANK I URLAUB", "2026-08-05", "2026-08-10", EntrySource.System, Guid.NewGuid())], Kinds).ShouldBeEmpty();
    }

    [Fact]
    public void Periods_over_a_year_end_and_leap_day_stay_intact()
    {
        var items = UnavailabilityTimeline.Build(Erika, [], [Entry("ELTERNZEIT", "2027-12-20", "2028-03-01")], Kinds);

        items.Single().ShouldMatch("ELTERNZEIT", D("2027-12-20"), D("2028-03-01"));
    }

    [Fact]
    public void Items_are_sorted_by_start_date()
    {
        var items = UnavailabilityTimeline.Build(Erika, [Vacation("2026-10-01", "2026-10-05")], [Entry("FREI", "2026-09-01", "2026-09-01")], Kinds);

        items.Select(i => i.KindCode).ShouldBe(["FREI", "URLAUB"]);
    }

    [Fact]
    public void Neutralised_periods_merge_touching_and_overlapping_items_so_no_kind_can_be_guessed_from_the_split()
    {
        var sick = Sick("2026-08-01", "2026-08-20");
        var ku = Entry("KRANK I URLAUB", "2026-08-05", "2026-08-10", EntrySource.System, sick.Id);
        var items = UnavailabilityTimeline.Build(Erika, [sick, Vacation("2026-09-01", "2026-09-05")], [ku, Entry("FREI", "2026-09-06", "2026-09-06"), Entry("FREI", "2026-12-24", "2027-01-02")], Kinds);

        UnavailabilityTimeline.Neutralise(items).Select(p => (p.From, p.To)).ShouldBe([
            (D("2026-08-01"), (DateOnly?)D("2026-08-20")),
            (D("2026-09-01"), D("2026-09-06")),
            (D("2026-12-24"), D("2027-01-02"))]);
    }

    [Fact]
    public void Colleagues_see_sickness_vacation_and_KU_by_kind_and_the_rest_neutral_and_merged()
    {
        var sick = Sick("2026-08-01", "2026-08-20");
        var ku = Entry("KRANK I URLAUB", "2026-08-05", "2026-08-10", EntrySource.System, sick.Id);
        var items = UnavailabilityTimeline.Build(
            Erika, [sick, Vacation("2026-09-01", "2026-09-05")], [ku, Entry("REHA", "2026-10-01", "2026-10-02"), Entry("FREI", "2026-10-03", "2026-10-03")], Kinds);

        UnavailabilityTimeline.ForColleagues(items).Select(c => (c.KindCode, c.From, c.To)).ShouldBe([
            ("KRANK", D("2026-08-01"), (DateOnly?)D("2026-08-04")),
            ("KRANK I URLAUB", D("2026-08-05"), D("2026-08-10")),
            ("KRANK", D("2026-08-11"), D("2026-08-20")),
            ("URLAUB", D("2026-09-01"), D("2026-09-05")),
            (null, D("2026-10-01"), D("2026-10-03"))]);
    }

    [Fact]
    public void An_open_ended_item_keeps_the_neutralised_period_open()
    {
        var items = UnavailabilityTimeline.Build(Erika, [Sick("2026-08-01", null)], [Entry("FREI", "2026-09-01", "2026-09-02")], Kinds);

        UnavailabilityTimeline.Neutralise(items).Select(p => (p.From, p.To)).ShouldBe([(D("2026-08-01"), (DateOnly?)null)]);
    }
}

internal static class TimelineAssertions
{
    public static void ShouldMatch(this UnavailabilityItem item, string code, DateOnly from, DateOnly to) =>
        (item.KindCode, item.From, item.To).ShouldBe((code, from, (DateOnly?)to));
}
