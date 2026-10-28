using Workforce.Core.Absences;

namespace Workforce.Core.WorkingTime;

/// <summary>The account of a person as HR sees it: the state today, the bookings (newest first) and the month closes.</summary>
public sealed record WorkingTimeView(AccountState State, IReadOnlyList<TimeBooking> Bookings, IReadOnlyList<MonthClose> Closes, WorkingTimeRules Rules);

/// <summary>
/// The working time account ("Arbeitszeitkonto"): opening balance, month close (overtime above the monthly threshold of the employment relationship),
/// time off, payout, payout at exit and corrections. Callers check authorization first and save the unit of work afterwards. Business failures are
/// returned as <see cref="WorkingTimeError"/>; nothing is stored then. The balance is computed from the bookings (<see cref="WorkingTimeAccount"/>), so
/// the rules HR sets apply to everything booked. Hours come from HR until the time tracking exists. No message leaves the service.
/// TODO(BC-Modell): the mirror of the account to BC is not defined yet.
/// TODO(Entscheidung): whether hours below the monthly threshold become minus hours is open; until then a month below the threshold books nothing.
/// </summary>
public sealed class WorkingTimeService(
    IWorkingTimeStore store,
    IAbsenceStore absences,
    IPeopleDirectory people,
    IEmploymentLookup employment,
    TimeProvider time,
    WorkforceOptions options)
{
    private const decimal MaxHours = 10000;
    private const decimal MaxHoursPerMonth = 744; // 31 days of 24 hours

    /// <summary>Today's date in the local time zone: the day changes at local midnight, also on summer/winter time days.</summary>
    public DateOnly TodayLocal => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.Zone).DateTime);

    // ------------------------------------------------------------------ view

    public async Task<WorkingTimeView?> GetAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (await people.GetAsync(employeeId, cancellationToken) is null)
        {
            return null;
        }

        var rules = (await GetRulesAsync(cancellationToken)).Rules;
        var bookings = await store.BookingsAsync(employeeId, cancellationToken);
        return new WorkingTimeView(
            WorkingTimeAccount.Compute(bookings, rules, TodayLocal),
            [.. bookings.OrderByDescending(b => b.Date).ThenByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)],
            [.. (await store.ClosesAsync(employeeId, cancellationToken)).OrderByDescending(c => c.Year).ThenByDescending(c => c.Month)],
            rules);
    }

    // ------------------------------------------------------------------ bookings

    /// <summary>HR enters the balance the account starts with (plus or minus), once per person.</summary>
    public async Task<BookingResult> SetOpeningBalanceAsync(Guid employeeId, decimal hours, DateOnly date, Guid actorId, CancellationToken cancellationToken)
    {
        if (!ValidSigned(hours))
        {
            return new BookingResult(WorkingTimeError.InvalidHours);
        }

        if (await Prepare(employeeId, date, cancellationToken) is { } failure)
        {
            return new BookingResult(failure);
        }

        if ((await store.BookingsAsync(employeeId, cancellationToken)).Any(b => b.Kind == BookingKind.OpeningBalance))
        {
            return new BookingResult(WorkingTimeError.OpeningBalanceExists);
        }

        return new BookingResult(WorkingTimeError.None, Add(employeeId, BookingKind.OpeningBalance, hours, date, null, actorId));
    }

    /// <summary>A later change of the balance (plus or minus), e.g. after wrong time tracking.</summary>
    public async Task<BookingResult> CorrectAsync(Guid employeeId, decimal hours, DateOnly date, Guid actorId, CancellationToken cancellationToken)
    {
        if (!ValidSigned(hours))
        {
            return new BookingResult(WorkingTimeError.InvalidHours);
        }

        if (await Prepare(employeeId, date, cancellationToken) is { } failure)
        {
            return new BookingResult(failure);
        }

        return new BookingResult(WorkingTimeError.None, Add(employeeId, BookingKind.Correction, hours, date, null, actorId));
    }

    /// <summary>Compensatory time off for <paramref name="date"/> (also a later day, planned): the balance on that day must cover it.</summary>
    public async Task<BookingResult> BookTimeOffAsync(Guid employeeId, decimal hours, DateOnly date, Guid actorId, CancellationToken cancellationToken)
    {
        if (!ValidPositive(hours))
        {
            return new BookingResult(WorkingTimeError.InvalidHours);
        }

        if (await Prepare(employeeId, date, cancellationToken, allowFuture: true) is { } failure)
        {
            return new BookingResult(failure);
        }

        var state = await StateAsync(employeeId, date, cancellationToken);
        return state.Balance < hours
            ? new BookingResult(WorkingTimeError.InsufficientBalance)
            : new BookingResult(WorkingTimeError.None, Add(employeeId, BookingKind.TimeOff, -hours, date, null, actorId));
    }

    /// <summary>Pays out overtime: the balance must reach the payout threshold HR set (default 0, so any balance) and cover the hours.</summary>
    public async Task<BookingResult> PayoutAsync(Guid employeeId, decimal hours, DateOnly date, Guid actorId, CancellationToken cancellationToken)
    {
        if (!ValidPositive(hours))
        {
            return new BookingResult(WorkingTimeError.InvalidHours);
        }

        if (await Prepare(employeeId, date, cancellationToken) is { } failure)
        {
            return new BookingResult(failure);
        }

        var state = await StateAsync(employeeId, date, cancellationToken);
        var rules = (await GetRulesAsync(cancellationToken)).Rules;
        if (state.Balance < rules.PayoutThresholdHours)
        {
            return new BookingResult(WorkingTimeError.BelowPayoutThreshold);
        }

        return state.Balance < hours
            ? new BookingResult(WorkingTimeError.InsufficientBalance)
            : new BookingResult(WorkingTimeError.None, Add(employeeId, BookingKind.Payout, -hours, date, null, actorId));
    }

    /// <summary>When a person leaves the whole remaining balance is paid out, whatever the threshold. Booked for the exit date.</summary>
    public async Task<BookingResult> PayoutAtExitAsync(Guid employeeId, Guid actorId, CancellationToken cancellationToken)
    {
        if (await people.GetAsync(employeeId, cancellationToken) is not { } person)
        {
            return new BookingResult(WorkingTimeError.UnknownPerson);
        }

        if (person.ExitDate is not { } exit)
        {
            return new BookingResult(WorkingTimeError.NotLeaving);
        }

        await absences.LockEmployeeAsync(employeeId, cancellationToken);
        var state = await StateAsync(employeeId, exit, cancellationToken);
        return state.Balance <= 0
            ? new BookingResult(WorkingTimeError.NothingToPay)
            : new BookingResult(WorkingTimeError.None, Add(employeeId, BookingKind.PayoutAtExit, -state.Balance, exit, null, actorId));
    }

    // ------------------------------------------------------------------ month close

    /// <summary>
    /// Closes a month that is over: hours above the monthly threshold of the person's employment relationship become overtime, 1:1, booked for the
    /// last day of the month (that date decides when they expire). Below the threshold nothing is booked.
    /// </summary>
    public async Task<MonthCloseResult> CloseMonthAsync(Guid employeeId, int year, int month, decimal workedHours, Guid actorId, CancellationToken cancellationToken)
    {
        if (await people.GetAsync(employeeId, cancellationToken) is null)
        {
            return new MonthCloseResult(WorkingTimeError.UnknownPerson);
        }

        if (year is < 2000 or > 2200 || month is < 1 or > 12)
        {
            return new MonthCloseResult(WorkingTimeError.InvalidPeriod);
        }

        if (workedHours is < 0 or > MaxHoursPerMonth || decimal.Round(workedHours, 2) != workedHours)
        {
            return new MonthCloseResult(WorkingTimeError.InvalidHours);
        }

        var lastDay = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        if (TodayLocal <= lastDay)
        {
            return new MonthCloseResult(WorkingTimeError.MonthNotFinished);
        }

        await absences.LockEmployeeAsync(employeeId, cancellationToken);
        if (await store.CloseAsync(employeeId, year, month, cancellationToken) is not null)
        {
            return new MonthCloseResult(WorkingTimeError.AlreadyClosed);
        }

        if (await employment.RelationshipOfAsync(employeeId, cancellationToken) is not { } relationship)
        {
            return new MonthCloseResult(WorkingTimeError.NoEmploymentRelationship);
        }

        if (await store.ThresholdAsync(relationship, cancellationToken) is not { } threshold)
        {
            return new MonthCloseResult(WorkingTimeError.NoThreshold);
        }

        var now = time.GetUtcNow();
        var overtime = Math.Max(0, workedHours - threshold);
        var close = new MonthClose
        {
            Id = Guid.CreateVersion7(now), EmployeeId = employeeId, Year = year, Month = month, WorkedHours = workedHours,
            ThresholdHours = threshold, OvertimeHours = overtime, ClosedBy = actorId, ClosedAt = now,
        };
        store.AddClose(close);
        var booking = overtime > 0 ? Add(employeeId, BookingKind.Overtime, overtime, lastDay, close.Id, actorId) : null;
        return new MonthCloseResult(WorkingTimeError.None, close, booking);
    }

    // ------------------------------------------------------------------ rules (HR, for everybody)

    /// <summary>The rules in force: what HR set, otherwise payout always possible and nothing expires.</summary>
    public async Task<(WorkingTimeRules Rules, bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy)> GetRulesAsync(CancellationToken cancellationToken) =>
        await store.GetRulesAsync(cancellationToken) is { } set ? (set.Rules, false, set.ChangedAt, set.ChangedBy) : (WorkingTimeRules.Default, true, null, null);

    public async Task<WorkingTimeError> SetRulesAsync(WorkingTimeRules rules, Guid actorId, CancellationToken cancellationToken)
    {
        if (!rules.IsValid)
        {
            return WorkingTimeError.InvalidRules;
        }

        await store.SetRulesAsync(new WorkingTimeRulesSetting(rules, time.GetUtcNow(), actorId), cancellationToken);
        return WorkingTimeError.None;
    }

    public async Task<IReadOnlyList<MonthlyThreshold>> ListThresholdsAsync(CancellationToken cancellationToken) => await store.ThresholdsAsync(cancellationToken);

    /// <summary>Sets the monthly threshold of an employment relationship (hours per month); <c>null</c> removes it.</summary>
    public async Task<WorkingTimeError> SetThresholdAsync(Guid relationshipId, decimal? hours, Guid actorId, CancellationToken cancellationToken)
    {
        if (!await employment.IsRelationshipAsync(relationshipId, cancellationToken))
        {
            return WorkingTimeError.UnknownRelationship;
        }

        if (hours is { } value && (value is < 0 or > MaxHoursPerMonth || decimal.Round(value, 2) != value))
        {
            return WorkingTimeError.InvalidHours;
        }

        await store.SetThresholdAsync(relationshipId, hours, actorId, time.GetUtcNow(), cancellationToken);
        return WorkingTimeError.None;
    }

    // ------------------------------------------------------------------ helpers

    private static bool ValidSigned(decimal hours) => hours != 0 && Math.Abs(hours) <= MaxHours && decimal.Round(hours, 2) == hours;

    private static bool ValidPositive(decimal hours) => hours > 0 && ValidSigned(hours);

    /// <summary>Person known, date sensible (not before 2000, not in the future unless allowed), then the person is locked against parallel bookings.</summary>
    private async Task<WorkingTimeError?> Prepare(Guid employeeId, DateOnly date, CancellationToken cancellationToken, bool allowFuture = false)
    {
        if (await people.GetAsync(employeeId, cancellationToken) is null)
        {
            return WorkingTimeError.UnknownPerson;
        }

        if (date.Year < 2000 || (!allowFuture && date > TodayLocal) || date.Year > 2200)
        {
            return WorkingTimeError.InvalidPeriod;
        }

        await absences.LockEmployeeAsync(employeeId, cancellationToken);
        return null;
    }

    private async Task<AccountState> StateAsync(Guid employeeId, DateOnly asOf, CancellationToken cancellationToken) =>
        WorkingTimeAccount.Compute(await store.BookingsAsync(employeeId, cancellationToken), (await GetRulesAsync(cancellationToken)).Rules, asOf);

    private TimeBooking Add(Guid employeeId, BookingKind kind, decimal hours, DateOnly date, Guid? monthCloseId, Guid actorId)
    {
        var now = time.GetUtcNow();
        var booking = new TimeBooking
        {
            Id = Guid.CreateVersion7(now), EmployeeId = employeeId, Kind = kind, Hours = hours, Date = date, MonthCloseId = monthCloseId, CreatedBy = actorId, CreatedAt = now,
        };
        store.AddBooking(booking);
        return booking;
    }
}
