using Microsoft.Extensions.Time.Testing;
using Workforce.Core.Absences;
using Workforce.Core.WorkingTime;

namespace Workforce.Core.Tests;

internal sealed class FakeWorkingTimeStore : IWorkingTimeStore
{
    public List<TimeBooking> Bookings { get; } = [];
    public List<MonthClose> Closes { get; } = [];
    public WorkingTimeRulesSetting? Rules { get; set; }
    public Dictionary<Guid, decimal> Thresholds { get; } = [];

    public Task<IReadOnlyList<TimeBooking>> BookingsAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TimeBooking>>([.. Bookings.Where(b => b.EmployeeId == employeeId)]);

    public void AddBooking(TimeBooking booking) => Bookings.Add(booking);

    public Task<IReadOnlyList<MonthClose>> ClosesAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MonthClose>>([.. Closes.Where(c => c.EmployeeId == employeeId)]);

    public Task<MonthClose?> CloseAsync(Guid employeeId, int year, int month, CancellationToken cancellationToken) =>
        Task.FromResult(Closes.FirstOrDefault(c => c.EmployeeId == employeeId && c.Year == year && c.Month == month));

    public void AddClose(MonthClose close) => Closes.Add(close);

    public Task<WorkingTimeRulesSetting?> GetRulesAsync(CancellationToken cancellationToken) => Task.FromResult(Rules);

    public Task SetRulesAsync(WorkingTimeRulesSetting setting, CancellationToken cancellationToken)
    {
        Rules = setting;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MonthlyThreshold>> ThresholdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MonthlyThreshold>>([.. Thresholds.Select(t => new MonthlyThreshold(t.Key, t.Value))]);

    public Task<decimal?> ThresholdAsync(Guid relationshipId, CancellationToken cancellationToken) =>
        Task.FromResult(Thresholds.TryGetValue(relationshipId, out var h) ? h : (decimal?)null);

    public Task SetThresholdAsync(Guid relationshipId, decimal? hours, Guid changedBy, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (hours is { } value)
        {
            Thresholds[relationshipId] = value;
        }
        else
        {
            Thresholds.Remove(relationshipId);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeEmployment : IEmploymentLookup
{
    public Dictionary<Guid, Guid> Relationship { get; } = [];
    public HashSet<Guid> Known { get; } = [];

    public Task<Guid?> RelationshipOfAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(Relationship.TryGetValue(employeeId, out var r) ? r : (Guid?)null);

    public Task<bool> IsRelationshipAsync(Guid entryId, CancellationToken cancellationToken) => Task.FromResult(Known.Contains(entryId));
}

public class WorkingTimeServiceTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Hanna = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid FullTime = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-000000000017");

    private readonly FakeWorkingTimeStore _store = new();
    private readonly FakeStore _absences = new();
    private readonly FakePeople _people = new();
    private readonly FakeEmployment _employment = new();

    // Wednesday 2026-10-28, 10:00 local (winter time)
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));

    public WorkingTimeServiceTests()
    {
        _people.People[Erika] = new PersonInfo(Erika, "Erika Mustermann", Store, null);
        _employment.Relationship[Erika] = FullTime;
        _employment.Known.Add(FullTime);
        _store.Thresholds[FullTime] = 160;
    }

    private WorkingTimeService Service() => new(_store, _absences, _people, _employment, _time, new WorkforceOptions());

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private async Task Overtime(string date, decimal hours) =>
        (await Service().CorrectAsync(Erika, hours, D(date), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.None);

    // ---------------------------------------------------------------- opening balance and corrections

    [Theory]
    [InlineData(12.5)]
    [InlineData(-8)]
    public async Task HR_enters_the_opening_balance_once_plus_or_minus(double hours)
    {
        var result = await Service().SetOpeningBalanceAsync(Erika, (decimal)hours, D("2026-10-01"), Hanna, CancellationToken.None);

        result.Error.ShouldBe(WorkingTimeError.None);
        var booking = result.Booking!;
        (booking.Kind, booking.Hours, booking.Date, booking.CreatedBy, booking.CreatedAt).ShouldBe((BookingKind.OpeningBalance, (decimal)hours, D("2026-10-01"), Hanna, _time.GetUtcNow()));
        (await Service().SetOpeningBalanceAsync(Erika, 1, D("2026-10-02"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.OpeningBalanceExists);
        _store.Bookings.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000.5)]
    [InlineData(1.005)]
    public async Task Hours_must_be_nonzero_limited_and_have_at_most_two_decimals(double hours)
    {
        (await Service().SetOpeningBalanceAsync(Erika, (decimal)hours, D("2026-10-01"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidHours);
        (await Service().CorrectAsync(Erika, (decimal)hours, D("2026-10-01"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidHours);
        _store.Bookings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_persons_and_future_or_absurd_dates_are_refused()
    {
        (await Service().CorrectAsync(Guid.NewGuid(), 1, D("2026-10-01"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.UnknownPerson);
        (await Service().CorrectAsync(Erika, 1, D("2026-10-29"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidPeriod);
        (await Service().CorrectAsync(Erika, 1, D("1999-12-31"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidPeriod);
        (await Service().CorrectAsync(Erika, 1, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.None); // today counts
    }

    // ---------------------------------------------------------------- month close

    [Fact]
    public async Task Hours_above_the_monthly_threshold_of_the_employment_relationship_become_overtime_1_to_1()
    {
        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 172, Hanna, CancellationToken.None);

        result.Error.ShouldBe(WorkingTimeError.None);
        var close = result.Close!;
        (close.WorkedHours, close.ThresholdHours, close.OvertimeHours, close.ClosedBy, close.ClosedAt).ShouldBe((172m, 160m, 12m, Hanna, _time.GetUtcNow()));
        var booking = result.Booking!;
        (booking.Kind, booking.Hours, booking.Date, booking.MonthCloseId).ShouldBe((BookingKind.Overtime, 12m, D("2026-09-30"), close.Id));
        (await Service().GetAsync(Erika, CancellationToken.None))!.State.Balance.ShouldBe(12m);
    }

    [Theory]
    [InlineData(160)]
    [InlineData(120)]
    public async Task At_or_below_the_threshold_the_month_is_closed_without_overtime(double worked)
    {
        // By default (HR has not switched minus hours on) nothing is booked below the threshold.
        var result = await Service().CloseMonthAsync(Erika, 2026, 9, (decimal)worked, Hanna, CancellationToken.None);

        result.Error.ShouldBe(WorkingTimeError.None);
        (result.Close!.OvertimeHours, result.Booking).ShouldBe((0m, null));
        _store.Bookings.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- minus hours (HR switches them on, default: off)

    private Task<WorkingTimeError> MinusOn() => Service().SetRulesAsync(new WorkingTimeRules(0, null, BookMinusHours: true), Hanna, CancellationToken.None);

    [Fact]
    public async Task Minus_hours_are_off_by_default()
    {
        (await Service().GetRulesAsync(CancellationToken.None)).Rules.BookMinusHours.ShouldBeFalse();
        WorkingTimeRules.Default.BookMinusHours.ShouldBeFalse();
    }

    [Fact]
    public async Task With_minus_hours_on_hours_below_the_monthly_threshold_are_booked_as_minus_hours_1_to_1()
    {
        await MinusOn();

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 148.5m, Hanna, CancellationToken.None);

        result.Error.ShouldBe(WorkingTimeError.None);
        var close = result.Close!;
        (close.WorkedHours, close.ThresholdHours, close.OvertimeHours, close.MinusHours).ShouldBe((148.5m, 160m, 0m, 11.5m));
        var booking = result.Booking!;
        (booking.Kind, booking.Hours, booking.Date, booking.MonthCloseId).ShouldBe((BookingKind.MinusHours, -11.5m, D("2026-09-30"), close.Id));
        var state = (await Service().GetAsync(Erika, CancellationToken.None))!.State;
        (state.Balance, state.Deficit).ShouldBe((-11.5m, 11.5m));
    }

    [Theory]
    [InlineData(160)]   // exactly the threshold: nothing
    [InlineData(172)]   // above: overtime, no minus
    public async Task With_minus_hours_on_a_month_at_or_above_the_threshold_books_no_minus_hours(double worked)
    {
        await MinusOn();

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, (decimal)worked, Hanna, CancellationToken.None);

        result.Close!.MinusHours.ShouldBe(0m);
        _store.Bookings.ShouldAllBe(b => b.Kind != BookingKind.MinusHours);
    }

    [Fact]
    public async Task A_later_month_with_overtime_covers_the_minus_hours_first()
    {
        await MinusOn();
        await Service().CloseMonthAsync(Erika, 2026, 8, 150, Hanna, CancellationToken.None);   // 10 minus
        await Service().CloseMonthAsync(Erika, 2026, 9, 172, Hanna, CancellationToken.None);   // 12 overtime

        var state = (await Service().GetAsync(Erika, CancellationToken.None))!.State;

        (state.Balance, state.Deficit).ShouldBe((2m, 0m));
    }

    [Fact]
    public async Task Minus_hours_use_up_the_oldest_overtime_first()
    {
        await MinusOn();
        await Overtime("2026-07-31", 20);
        await Service().CloseMonthAsync(Erika, 2026, 8, 150, Hanna, CancellationToken.None);   // 10 minus: from the July overtime

        var state = (await Service().GetAsync(Erika, CancellationToken.None))!.State;

        (state.Balance, state.Deficit).ShouldBe((10m, 0m));
        state.Buckets.ShouldHaveSingleItem().Remaining.ShouldBe(10m);
    }

    [Fact]
    public async Task Switching_minus_hours_on_later_changes_only_the_months_closed_afterwards()
    {
        await Service().CloseMonthAsync(Erika, 2026, 8, 150, Hanna, CancellationToken.None);   // off: nothing booked
        await MinusOn();
        await Service().CloseMonthAsync(Erika, 2026, 9, 150, Hanna, CancellationToken.None);   // on: 10 minus

        _store.Bookings.ShouldHaveSingleItem().Hours.ShouldBe(-10m);
        (await Service().GetAsync(Erika, CancellationToken.None))!.Closes.Select(c => (c.Month, c.MinusHours)).ShouldBe([(9, 10m), (8, 0m)]);
    }

    [Theory]
    [InlineData(2026, 12, "2026-12-31")]   // year end
    [InlineData(2028, 2, "2028-02-29")]    // leap day
    [InlineData(2026, 10, "2026-10-31")]   // the month of the change back to winter time (not over yet today? 28 Oct: see below)
    public async Task Minus_hours_count_for_the_last_day_of_the_month(int year, int month, string expectedDate)
    {
        if (year == 2026 && month == 10)
        {
            _time.Advance(TimeSpan.FromDays(4)); // 1 November: October is over
        }
        else
        {
            _time.SetUtcNow(new DateTimeOffset(year + (month == 12 ? 1 : 0), month == 12 ? 1 : month + 1, 5, 9, 0, 0, TimeSpan.Zero));
        }

        await MinusOn();

        var result = await Service().CloseMonthAsync(Erika, year, month, 100, Hanna, CancellationToken.None);

        (result.Error, result.Booking!.Date).ShouldBe((WorkingTimeError.None, D(expectedDate)));
    }

    // ---------------------------------------------------------------- the lower limit of the balance (HR, default: none)

    private Task<WorkingTimeError> MinusOn(decimal? limit) => Service().SetRulesAsync(new WorkingTimeRules(0, null, BookMinusHours: true, MaxMinusHours: limit), Hanna, CancellationToken.None);

    [Fact]
    public async Task Without_a_limit_the_balance_can_go_as_far_into_the_minus_as_the_months_say()
    {
        await MinusOn(null);

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 0, Hanna, CancellationToken.None);

        (result.Close!.MinusHours, result.Close.MinusHoursSkipped).ShouldBe((160m, 0m));
    }

    [Fact]
    public async Task A_limit_stops_the_minus_hours_at_the_lowest_balance_HR_allows_and_reports_what_was_not_booked()
    {
        await MinusOn(20);

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 130, Hanna, CancellationToken.None); // 30 missing

        (result.Close!.MinusHours, result.Close.MinusHoursSkipped).ShouldBe((20m, 10m));
        result.Booking!.Hours.ShouldBe(-20m);
        (await Service().GetAsync(Erika, CancellationToken.None))!.State.Balance.ShouldBe(-20m);
    }

    [Fact]
    public async Task The_limit_counts_from_the_balance_so_a_positive_balance_leaves_room()
    {
        await MinusOn(20);
        await Overtime("2026-07-31", 5);

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 130, Hanna, CancellationToken.None); // 30 missing, room: 5 + 20

        (result.Close!.MinusHours, result.Close.MinusHoursSkipped).ShouldBe((25m, 5m));
        (await Service().GetAsync(Erika, CancellationToken.None))!.State.Balance.ShouldBe(-20m);
    }

    [Fact]
    public async Task At_the_limit_nothing_is_booked_but_the_month_is_closed_and_the_gap_reported()
    {
        await MinusOn(20);
        await Service().CloseMonthAsync(Erika, 2026, 8, 130, Hanna, CancellationToken.None); // -20, at the limit

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 150, Hanna, CancellationToken.None); // 10 missing

        result.Error.ShouldBe(WorkingTimeError.None);
        (result.Close!.MinusHours, result.Close.MinusHoursSkipped, result.Booking).ShouldBe((0m, 10m, null));
        _store.Closes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_limit_of_zero_allows_minus_hours_only_up_to_the_positive_balance()
    {
        await MinusOn(0);
        await Overtime("2026-07-31", 8);

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 150, Hanna, CancellationToken.None); // 10 missing, 8 available

        (result.Close!.MinusHours, result.Close.MinusHoursSkipped).ShouldBe((8m, 2m));
        (await Service().GetAsync(Erika, CancellationToken.None))!.State.Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task A_limit_without_minus_hours_switched_on_does_nothing()
    {
        await Service().SetRulesAsync(new WorkingTimeRules(0, null, BookMinusHours: false, MaxMinusHours: 5), Hanna, CancellationToken.None);

        var result = await Service().CloseMonthAsync(Erika, 2026, 9, 130, Hanna, CancellationToken.None);

        (result.Close!.MinusHours, result.Close.MinusHoursSkipped, result.Booking).ShouldBe((0m, 0m, null));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(20, true)]
    [InlineData(1000, true)]
    [InlineData(-1, false)]
    [InlineData(1000.5, false)]
    [InlineData(1.005, false)]
    public void The_limit_must_be_sensible(double limit, bool valid) =>
        new WorkingTimeRules(0, null, true, (decimal)limit).IsValid.ShouldBe(valid);

    [Fact]
    public async Task The_minus_hours_switch_is_stored_with_the_rules_by_HR_for_everybody()
    {
        await Service().SetRulesAsync(new WorkingTimeRules(20, 6, BookMinusHours: true), Hanna, CancellationToken.None);

        var set = await Service().GetRulesAsync(CancellationToken.None);

        (set.Rules, set.IsDefault, set.ChangedBy).ShouldBe((new WorkingTimeRules(20, 6, BookMinusHours: true), false, (Guid?)Hanna));
    }

    [Fact]
    public async Task A_month_can_only_be_closed_once_it_is_over_and_only_once()
    {
        (await Service().CloseMonthAsync(Erika, 2026, 10, 170, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.MonthNotFinished); // 28 Oct: October runs to the 31st
        (await Service().CloseMonthAsync(Erika, 2026, 11, 170, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.MonthNotFinished);

        (await Service().CloseMonthAsync(Erika, 2026, 9, 170, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.None);
        (await Service().CloseMonthAsync(Erika, 2026, 9, 175, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.AlreadyClosed);
        _store.Bookings.Single().Hours.ShouldBe(10m);
    }

    [Fact]
    public async Task The_last_day_of_the_month_does_not_finish_it_but_the_day_after_does()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 31, 22, 30, 0, TimeSpan.Zero)); // 31 Oct 23:30 local (still winter time? summer time ended on 25 Oct)
        (await Service().CloseMonthAsync(Erika, 2026, 10, 170, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.MonthNotFinished);

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero)); // 1 Nov 00:30 local
        (await Service().CloseMonthAsync(Erika, 2026, 10, 170, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.None);
    }

    [Fact]
    public async Task The_overtime_of_February_in_a_leap_year_and_of_December_carry_the_last_day_of_their_month()
    {
        _time.SetUtcNow(new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.Zero));
        (await Service().CloseMonthAsync(Erika, 2026, 12, 165, Hanna, CancellationToken.None)).Booking!.Date.ShouldBe(D("2026-12-31"));

        _time.SetUtcNow(new DateTimeOffset(2028, 3, 1, 9, 0, 0, TimeSpan.Zero));
        (await Service().CloseMonthAsync(Erika, 2028, 2, 165, Hanna, CancellationToken.None)).Booking!.Date.ShouldBe(D("2028-02-29"));
    }

    [Fact]
    public async Task Without_an_employment_relationship_or_a_threshold_nothing_can_be_closed()
    {
        _store.Thresholds.Clear();
        (await Service().CloseMonthAsync(Erika, 2026, 9, 170, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.NoThreshold);

        _employment.Relationship.Clear();
        (await Service().CloseMonthAsync(Erika, 2026, 9, 170, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.NoEmploymentRelationship);
        _store.Closes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2026, 0, 100)]
    [InlineData(2026, 13, 100)]
    [InlineData(1999, 12, 100)]
    public async Task Close_needs_a_real_month(int year, int month, double worked) =>
        (await Service().CloseMonthAsync(Erika, year, month, (decimal)worked, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidPeriod);

    [Theory]
    [InlineData(-1)]
    [InlineData(745)]
    public async Task Worked_hours_must_fit_into_a_month(double worked) =>
        (await Service().CloseMonthAsync(Erika, 2026, 9, (decimal)worked, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidHours);

    // ---------------------------------------------------------------- time off and payout

    [Fact]
    public async Task Time_off_lowers_the_balance_and_needs_enough_hours()
    {
        await Overtime("2026-09-30", 10);

        (await Service().BookTimeOffAsync(Erika, 11, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InsufficientBalance);
        var result = await Service().BookTimeOffAsync(Erika, 4, D("2026-10-28"), Hanna, CancellationToken.None);

        (result.Error, result.Booking!.Kind, result.Booking.Hours).ShouldBe((WorkingTimeError.None, BookingKind.TimeOff, -4m));
        (await Service().GetAsync(Erika, CancellationToken.None))!.State.Balance.ShouldBe(6m);
        (await Service().BookTimeOffAsync(Erika, 0, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidHours);
        (await Service().BookTimeOffAsync(Erika, -3, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InvalidHours);
    }

    [Fact]
    public async Task Time_off_can_be_planned_for_a_later_day_if_the_balance_covers_it_then()
    {
        await Overtime("2026-09-30", 10);

        (await Service().BookTimeOffAsync(Erika, 6, D("2026-11-05"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.None);

        (await Service().GetAsync(Erika, CancellationToken.None))!.State.Balance.ShouldBe(10m); // not yet taken
    }

    [Fact]
    public async Task Hours_that_expire_before_the_day_of_the_time_off_do_not_count()
    {
        await Service().SetRulesAsync(new WorkingTimeRules(0, 1), Hanna, CancellationToken.None);
        await Overtime("2026-08-31", 10); // usable until 30 Sep

        (await Service().BookTimeOffAsync(Erika, 1, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InsufficientBalance);
    }

    [Fact]
    public async Task Payout_by_default_is_possible_at_any_balance()
    {
        await Overtime("2026-09-30", 5);

        var result = await Service().PayoutAsync(Erika, 5, D("2026-10-28"), Hanna, CancellationToken.None);

        (result.Error, result.Booking!.Kind, result.Booking.Hours).ShouldBe((WorkingTimeError.None, BookingKind.Payout, -5m));
        (await Service().PayoutAsync(Erika, 1, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.InsufficientBalance);
    }

    [Fact]
    public async Task Payout_needs_a_balance_at_least_as_high_as_the_threshold_HR_set()
    {
        await Service().SetRulesAsync(new WorkingTimeRules(20, null), Hanna, CancellationToken.None);
        await Overtime("2026-09-30", 15);

        (await Service().PayoutAsync(Erika, 5, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.BelowPayoutThreshold);

        await Overtime("2026-10-01", 10); // 25 hours
        (await Service().PayoutAsync(Erika, 5, D("2026-10-28"), Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.None);
    }

    [Fact]
    public async Task At_exit_the_whole_balance_is_paid_out_whatever_the_threshold()
    {
        await Service().SetRulesAsync(new WorkingTimeRules(100, null), Hanna, CancellationToken.None);
        await Overtime("2026-09-30", 7.5m);
        (await Service().PayoutAtExitAsync(Erika, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.NotLeaving);

        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-11-30") };
        var result = await Service().PayoutAtExitAsync(Erika, Hanna, CancellationToken.None);

        (result.Error, result.Booking!.Kind, result.Booking.Hours, result.Booking.Date).ShouldBe((WorkingTimeError.None, BookingKind.PayoutAtExit, -7.5m, D("2026-11-30")));
        (await Service().PayoutAtExitAsync(Erika, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.NothingToPay);
    }

    [Fact]
    public async Task At_exit_a_negative_or_empty_balance_pays_nothing()
    {
        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-11-30") };
        await Overtime("2026-09-30", -3);

        (await Service().PayoutAtExitAsync(Erika, Hanna, CancellationToken.None)).Error.ShouldBe(WorkingTimeError.NothingToPay);
    }

    // ---------------------------------------------------------------- the view, rules and thresholds

    [Fact]
    public async Task The_view_applies_the_expiry_rule_to_everything_already_booked()
    {
        await Overtime("2026-01-31", 10);
        await Overtime("2026-09-30", 4);
        (await Service().GetAsync(Erika, CancellationToken.None))!.State.Balance.ShouldBe(14m);

        await Service().SetRulesAsync(new WorkingTimeRules(0, 3), Hanna, CancellationToken.None);

        var view = (await Service().GetAsync(Erika, CancellationToken.None))!;
        (view.State.Balance, view.State.Forfeited).ShouldBe((4m, 10m));
        view.Bookings.Select(b => b.Date).ShouldBe([D("2026-09-30"), D("2026-01-31")]); // newest first
        (await Service().GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_rules_default_to_payout_always_and_nothing_expires_and_are_set_by_HR_for_everybody()
    {
        var initial = await Service().GetRulesAsync(CancellationToken.None);
        (initial.Rules, initial.IsDefault).ShouldBe((WorkingTimeRules.Default, true));

        (await Service().SetRulesAsync(new WorkingTimeRules(40, 12), Hanna, CancellationToken.None)).ShouldBe(WorkingTimeError.None);
        (await Service().SetRulesAsync(new WorkingTimeRules(-1, null), Hanna, CancellationToken.None)).ShouldBe(WorkingTimeError.InvalidRules);

        var set = await Service().GetRulesAsync(CancellationToken.None);
        (set.Rules, set.IsDefault, set.ChangedBy, set.ChangedAt).ShouldBe((new WorkingTimeRules(40, 12), false, (Guid?)Hanna, (DateTimeOffset?)_time.GetUtcNow()));
    }

    [Fact]
    public async Task The_monthly_threshold_is_set_per_employment_relationship_and_can_be_removed()
    {
        var other = Guid.Parse("00000000-0000-0000-0000-0000000000f2");
        _employment.Known.Add(other);

        (await Service().SetThresholdAsync(other, 80, Hanna, CancellationToken.None)).ShouldBe(WorkingTimeError.None);
        (await Service().ListThresholdsAsync(CancellationToken.None)).Select(t => (t.RelationshipId, t.Hours)).ShouldBe([(FullTime, 160m), (other, 80m)], ignoreOrder: true);
        (await Service().SetThresholdAsync(other, null, Hanna, CancellationToken.None)).ShouldBe(WorkingTimeError.None);
        (await Service().ListThresholdsAsync(CancellationToken.None)).Select(t => t.RelationshipId).ShouldBe([FullTime]);
        (await Service().SetThresholdAsync(Guid.NewGuid(), 80, Hanna, CancellationToken.None)).ShouldBe(WorkingTimeError.UnknownRelationship);
        (await Service().SetThresholdAsync(FullTime, 745, Hanna, CancellationToken.None)).ShouldBe(WorkingTimeError.InvalidHours);
        (await Service().SetThresholdAsync(FullTime, -1, Hanna, CancellationToken.None)).ShouldBe(WorkingTimeError.InvalidHours);
    }
}
