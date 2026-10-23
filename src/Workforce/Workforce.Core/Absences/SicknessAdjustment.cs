namespace Workforce.Core.Absences;

/// <summary>What remains of a vacation after sickness: the part before and the part after the sick days (either may be missing).</summary>
public sealed record VacationAdjustment(DateRange? Before, DateRange? After, bool IsUnchanged)
{
    public bool IsCompletelyCancelled => Before is null && After is null;
}

/// <summary>
/// Sickness during approved vacation: the vacation is cancelled for the affected days (and thereby credited to the vacation account).
/// The vacation is cut by calendar days; remaining pieces without a single working day are dropped.
/// </summary>
public static class SicknessAdjustment
{
    public static VacationAdjustment Plan(DateRange vacation, DateRange sickness, WorkCalendar calendar)
    {
        if (vacation.To is not { } vacationEnd)
        {
            throw new ArgumentException("A vacation always has an end.", nameof(vacation));
        }

        if (!vacation.Overlaps(sickness))
        {
            return new VacationAdjustment(vacation, null, IsUnchanged: true);
        }

        DateRange? before = sickness.From > vacation.From ? DateRange.Of(vacation.From, sickness.From.AddDays(-1)) : null;
        DateRange? after = sickness.To is { } sickEnd && sickEnd < vacationEnd ? DateRange.Of(sickEnd.AddDays(1), vacationEnd) : null;
        return new VacationAdjustment(KeepIfWorked(before, calendar), KeepIfWorked(after, calendar), IsUnchanged: false);
    }

    private static DateRange? KeepIfWorked(DateRange? range, WorkCalendar calendar) =>
        range is { To: { } to } r && calendar.CountWorkingDays(r.From, to) > 0 ? range : null;
}
