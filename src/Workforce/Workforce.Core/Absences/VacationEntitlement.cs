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

/// <summary>
/// Additional vacation days entered by HR on top of the contractual ones. They are used up <b>before</b> the contractual days and
/// expire at the end of March of the year after <see cref="Year"/> (the first quarter of the following year); what is unused by then is lost.
/// </summary>
/// <param name="Id">Identifier.</param>
/// <param name="Kind">Leftover or additional days.</param>
/// <param name="Year">The year the days belong to: the year the leftover comes from, or the year the additional days are granted for.</param>
/// <param name="Days">Days (halves allowed).</param>
public sealed record ExtraVacationDays(Guid Id, ExtraVacationKind Kind, int Year, decimal Days)
{
    /// <summary>First day they can be used.</summary>
    public DateOnly UsableFrom => Kind == ExtraVacationKind.CarryOver ? new DateOnly(Year + 1, 1, 1) : new DateOnly(Year, 1, 1);

    /// <summary>Last day they can be used: the end of the first quarter of the following year.</summary>
    public DateOnly ExpiresOn => new(Year + 1, 3, 31);
}

/// <summary>One group of additional days in the account of a year.</summary>
/// <param name="Id">Identifier of the additional days.</param>
/// <param name="Kind">Leftover or additional days.</param>
/// <param name="Year">Year they belong to.</param>
/// <param name="Days">Days as entered.</param>
/// <param name="AvailableInYear">What was still there at the start of the year (less what was used in an earlier year).</param>
/// <param name="UsedInYear">Used by vacation in the year.</param>
/// <param name="Forfeited">Lost: unused after the expiry date (only once the date the account is evaluated for lies behind it).</param>
/// <param name="ExpiresOn">Last day they can be used.</param>
public sealed record ExtraDaysLine(Guid Id, ExtraVacationKind Kind, int Year, decimal Days, decimal AvailableInYear, decimal UsedInYear, decimal Forfeited, DateOnly ExpiresOn);

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
    /// Contractual days of <paramref name="year"/>: in the year of joining the annual days divided by 12 times the full months of employment,
    /// rounded up to half days (and never more than the annual days); before the year of joining none; otherwise the annual days.
    /// An unknown entry date counts as employed all year.
    /// </summary>
    public static decimal Contractual(decimal annualDays, DateOnly? entry, int year)
    {
        if (entry is not { } joined || joined.Year < year)
        {
            return annualDays;
        }

        if (joined.Year > year)
        {
            return 0m;
        }

        return Math.Min(annualDays, RoundUpToHalf(annualDays / 12m * FullMonthsInEntryYear(joined)));
    }
}
