namespace Workforce.Core.Absences;

/// <summary>Why additional vacation days exist; decides from when they can be used.</summary>
public enum ExtraVacationKind
{
    Unknown = 0,

    /// <summary>Leftover (Resturlaub) of <see cref="ExtraVacationDays.Year"/>: usable from 1 January of the following year.</summary>
    CarryOver = 1,

    /// <summary>Additional days for <see cref="ExtraVacationDays.Year"/> (e.g. special leave): usable from 1 January of that year.</summary>
    Additional = 2,
}

/// <summary>What happens before the waiting period is over (the first months of employment).</summary>
public enum WaitingPeriodMode
{
    Unknown = 0,

    /// <summary>Only the days earned so far (annual days / 12 per complete month of employment) may be taken.</summary>
    Accrued = 1,

    /// <summary>No vacation may be taken before the waiting period is over.</summary>
    Blocked = 2,
}

/// <summary>How pro rata days (year of joining, changes during the year) are rounded.</summary>
public enum ProRataRounding
{
    Unknown = 0,
    Up = 1,
    Nearest = 2,
    Down = 3,

    /// <summary>No rounding.</summary>
    None = 4,
}

/// <summary>
/// The rules HR maintains for the vacation of everybody. Defaults: additional days never expire, no waiting period, pro rata days are
/// rounded up to half days.
/// </summary>
/// <param name="ExpiryMonth">Month of the expiry date of additional days (in the year after the year they belong to), <c>null</c> = never expire.</param>
/// <param name="ExpiryDay">Day of the expiry date, <c>null</c> = never expire.</param>
/// <param name="WaitingPeriodMonths">Months after the entry during which <paramref name="WaitingMode"/> applies; 0 = no waiting period.</param>
/// <param name="WaitingMode">What applies during the waiting period.</param>
/// <param name="Rounding">How pro rata days are rounded.</param>
/// <param name="RoundingStep">Rounding to half (0.5) or whole (1) days.</param>
public sealed record VacationRules(
    int? ExpiryMonth,
    int? ExpiryDay,
    int WaitingPeriodMonths = 0,
    WaitingPeriodMode WaitingMode = WaitingPeriodMode.Accrued,
    ProRataRounding Rounding = ProRataRounding.Up,
    decimal RoundingStep = 0.5m)
{
    /// <summary>The default: additional days never expire, no waiting period, rounding up to half days.</summary>
    public static readonly VacationRules NeverExpire = new(null, null);

    /// <summary>Expiry at the end of the first quarter of the following year (31 March).</summary>
    public static readonly VacationRules EndOfFirstQuarter = new(3, 31);

    /// <summary>Expiry both or none and a real calendar day (29 February is allowed and falls on 28 February in years without it); sensible waiting period and rounding.</summary>
    public bool IsValid => ExpiryIsValid && WaitingPeriodMonths is >= 0 and <= 24
        && Enum.IsDefined(WaitingMode) && WaitingMode != WaitingPeriodMode.Unknown
        && Enum.IsDefined(Rounding) && Rounding != ProRataRounding.Unknown
        && RoundingStep is 0.5m or 1m;

    private bool ExpiryIsValid => (ExpiryMonth, ExpiryDay) switch
    {
        (null, null) => true,
        ( { } m, { } d) => m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(2024, m),
        _ => false,
    };

    public bool ExtraDaysExpire => ExpiryMonth is not null && ExpiryDay is not null;

    /// <summary>The last usable day of additional days that belong to <paramref name="year"/>; <c>null</c> if they never expire.</summary>
    public DateOnly? ExpiryFor(int year) => ExpiryMonth is { } m && ExpiryDay is { } d
        ? new DateOnly(year + 1, m, Math.Min(d, DateTime.DaysInMonth(year + 1, m)))
        : null;

    /// <summary>Rounds pro rata days as the rule says.</summary>
    public decimal Round(decimal days) => Rounding switch
    {
        ProRataRounding.Up => Math.Ceiling(days / RoundingStep) * RoundingStep,
        ProRataRounding.Nearest => Math.Round(days / RoundingStep, MidpointRounding.AwayFromZero) * RoundingStep,
        ProRataRounding.Down => Math.Floor(days / RoundingStep) * RoundingStep,
        _ => days,
    };
}

/// <summary>A change of the annual entitlement during a year (e.g. other working days per week): applies from the first of the month on, from the month after the date if it falls inside a month.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="EffectiveFrom">Date the change takes effect; only counts in the year of this date.</param>
/// <param name="AnnualDays">Annual days that apply from then on.</param>
public sealed record EntitlementChange(Guid Id, DateOnly EffectiveFrom, decimal AnnualDays);

/// <summary>The rules HR has set, with who and when.</summary>
public sealed record VacationRulesSetting(VacationRules Rules, DateTimeOffset ChangedAt, Guid ChangedBy);

/// <summary>
/// Additional vacation days entered by HR on top of the contractual ones. They are used up <b>before</b> the contractual days and expire
/// as <see cref="VacationRules"/> says (by default never); what is unused by then is lost.
/// </summary>
/// <param name="Id">Identifier.</param>
/// <param name="Kind">Leftover or additional days.</param>
/// <param name="Year">The year the days belong to: the year the leftover comes from, or the year the additional days are granted for.</param>
/// <param name="Days">Days (halves allowed).</param>
public sealed record ExtraVacationDays(Guid Id, ExtraVacationKind Kind, int Year, decimal Days)
{
    /// <summary>First day they can be used.</summary>
    public DateOnly UsableFrom => Kind == ExtraVacationKind.CarryOver ? new DateOnly(Year + 1, 1, 1) : new DateOnly(Year, 1, 1);

    /// <summary>Last day they can be used under <paramref name="rules"/>; <c>null</c> if they never expire.</summary>
    public DateOnly? ExpiresOn(VacationRules rules) => rules.ExpiryFor(Year);
}

/// <summary>One group of additional days in the account of a year.</summary>
/// <param name="Id">Identifier of the additional days.</param>
/// <param name="Kind">Leftover or additional days.</param>
/// <param name="Year">Year they belong to.</param>
/// <param name="Days">Days as entered.</param>
/// <param name="AvailableInYear">What was still there at the start of the year (less what was used in an earlier year).</param>
/// <param name="UsedInYear">Used by vacation in the year.</param>
/// <param name="Forfeited">Lost: unused after the expiry date (only once the date the account is evaluated for lies behind it).</param>
/// <param name="ExpiresOn">Last day they can be used; <c>null</c> if they never expire.</param>
public sealed record ExtraDaysLine(Guid Id, ExtraVacationKind Kind, int Year, decimal Days, decimal AvailableInYear, decimal UsedInYear, decimal Forfeited, DateOnly? ExpiresOn);

/// <summary>The account of a year with every component, so that the calculation can be followed.</summary>
/// <param name="Year">The year.</param>
/// <param name="ContractualAnnualDays">Days per year according to the contract.</param>
/// <param name="ContractualDays">Contractual days of this year: pro rata in the year of joining, otherwise the annual days.</param>
/// <param name="ExtraDays">Additional days that count (available, without the forfeited ones).</param>
/// <param name="ForfeitedExtraDays">Additional days lost by expiry.</param>
/// <param name="EffectiveDays">Effective vacation days: contractual days plus additional days that count.</param>
/// <param name="Taken">Days taken (approved, before today).</param>
/// <param name="Planned">Days planned (approved from today, requested).</param>
/// <param name="Remaining">Effective days minus taken and planned; negative if the contract is lowered afterwards.</param>
/// <param name="Lines">The additional days with their use.</param>
public sealed record VacationBreakdown(
    int Year,
    decimal ContractualAnnualDays,
    decimal ContractualDays,
    decimal ExtraDays,
    decimal ForfeitedExtraDays,
    decimal EffectiveDays,
    decimal Taken,
    decimal Planned,
    decimal Remaining,
    IReadOnlyList<ExtraDaysLine> Lines)
{
    /// <summary>The summary shape used before additional days existed (<see cref="VacationBalance.Entitlement"/> = effective days).</summary>
    public VacationBalance ToBalance() => new(Year, EffectiveDays, Taken, Planned, Remaining);
}

/// <summary>Rules for the contractual entitlement of a year.</summary>
public static class VacationEntitlement
{
    /// <summary>Rounds up to the next half day (2.083 becomes 2.5, 23.33 becomes 23.5).</summary>
    public static decimal RoundUpToHalf(decimal days) => Math.Ceiling(days * 2m) / 2m;

    /// <summary>
    /// Full months of employment in the year of joining: the month of joining counts only if the person starts on its first day.
    /// </summary>
    public static int FullMonthsInEntryYear(DateOnly entry) => entry.Day == 1 ? 13 - entry.Month : 12 - entry.Month;

    /// <summary>
    /// Contractual days of <paramref name="year"/>. Without joining in the year and without changes: the annual days as entered. Otherwise
    /// the annual days are spread over the months (1/12 each, the annual days in force at the start of the month, see
    /// <see cref="EntitlementChange"/>), only the full months of employment count, and the sum is rounded as <paramref name="rules"/> say
    /// (default: up to half days) but never exceeds the highest annual days. Before the year of joining there are none.
    /// An unknown entry date counts as employed all year.
    /// </summary>
    public static decimal Contractual(
        decimal annualDays, DateOnly? entry, int year, VacationRules? rules = null, IReadOnlyList<EntitlementChange>? changes = null)
    {
        if (entry is { } joinedLater && joinedLater.Year > year)
        {
            return 0m;
        }

        var inYear = (changes ?? []).Where(c => c.EffectiveFrom.Year == year).OrderBy(c => c.EffectiveFrom).ToList();
        var joinedThisYear = entry is { } e && e.Year == year;
        if (!joinedThisYear && inYear.Count == 0)
        {
            return annualDays;
        }

        var firstMonth = joinedThisYear ? (entry!.Value.Day == 1 ? entry.Value.Month : entry.Value.Month + 1) : 1;
        decimal sumOfAnnualDays = 0;
        for (var month = firstMonth; month <= 12; month++)
        {
            sumOfAnnualDays += AnnualDaysIn(annualDays, inYear, new DateOnly(year, month, 1));
        }

        var highest = Math.Max(annualDays, inYear.Count == 0 ? 0m : inYear.Max(c => c.AnnualDays));
        return Math.Min(highest, (rules ?? VacationRules.NeverExpire).Round(sumOfAnnualDays / 12m));
    }

    /// <summary>The annual days in force on <paramref name="day"/>: the latest change up to then, otherwise the base value.</summary>
    private static decimal AnnualDaysIn(decimal annualDays, IReadOnlyList<EntitlementChange> changesOfTheYear, DateOnly day) =>
        changesOfTheYear.LastOrDefault(c => c.EffectiveFrom <= day)?.AnnualDays ?? annualDays;

    /// <summary>Days earned up to <paramref name="day"/> in the first months of employment: annual days / 12 per complete month since the entry (anniversary of the entry day), rounded as <paramref name="rules"/> say.</summary>
    public static decimal Earned(decimal annualDays, DateOnly entry, DateOnly day, VacationRules rules)
    {
        var months = (day.Year - entry.Year) * 12 + day.Month - entry.Month - (day.Day < entry.Day ? 1 : 0);
        return months <= 0 ? 0m : rules.Round(annualDays / 12m * months);
    }
}

/// <summary>The waiting period: in the first months of employment only the days earned so far (or none) may be taken.</summary>
public static class VacationWaitingPeriod
{
    /// <summary>
    /// Checks requested vacation days against the waiting period. Only days before the end of the waiting period (entry plus the months)
    /// are restricted. <c>true</c> if all are allowed.
    /// </summary>
    /// <param name="entry">Date of joining.</param>
    /// <param name="rules">The rules (waiting period months and mode).</param>
    /// <param name="annualDaysOfYear">The annual days of a year; <c>null</c> if unknown (then nothing is restricted here).</param>
    /// <param name="existingDays">Vacation days already booked or requested (working days).</param>
    /// <param name="requestedDays">The requested working days.</param>
    public static bool Allows(
        DateOnly entry, VacationRules rules, Func<int, decimal?> annualDaysOfYear, IEnumerable<DateOnly> existingDays, IEnumerable<DateOnly> requestedDays)
    {
        if (rules.WaitingPeriodMonths <= 0)
        {
            return true;
        }

        var end = entry.AddMonths(rules.WaitingPeriodMonths);
        var requested = requestedDays.Where(d => d < end).OrderBy(d => d).ToList();
        if (requested.Count == 0)
        {
            return true;
        }

        if (rules.WaitingMode == WaitingPeriodMode.Blocked)
        {
            return false;
        }

        var existing = existingDays.Where(d => d < end).ToList();
        foreach (var day in requested)
        {
            if (annualDaysOfYear(day.Year) is not { } annual)
            {
                continue;
            }

            var used = existing.Count(d => d <= day) + requested.Count(d => d <= day);
            if (used > VacationEntitlement.Earned(annual, entry, day, rules))
            {
                return false;
            }
        }

        return true;
    }
}
