namespace Workforce.Core.Absences;

/// <summary>Days of the week that count as working days; default Monday to Saturday (configurable, plan: "Arbeitstage Mo–Sa").</summary>
public sealed record WorkingWeek(IReadOnlySet<DayOfWeek> Days)
{
    public static readonly WorkingWeek MondayToSaturday = new(new HashSet<DayOfWeek>
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    });

    public bool IsWorkingDay(DateOnly date) => Days.Contains(date.DayOfWeek);
}

/// <summary>Working days of a person: the working week without statutory holidays of the applicable state.</summary>
public sealed class WorkCalendar(WorkingWeek week, IHolidayCalendar holidays, string? state)
{
    public bool IsWorkingDay(DateOnly date) => week.IsWorkingDay(date) && !holidays.IsHoliday(date, state);

    public int CountWorkingDays(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("The end of a period cannot lie before its start.", nameof(to));
        }

        var count = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (IsWorkingDay(day))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Working days per calendar year (a vacation across the year end is booked against both years).</summary>
    public IReadOnlyDictionary<int, int> CountWorkingDaysByYear(DateOnly from, DateOnly to)
    {
        var result = new Dictionary<int, int>();
        for (var year = from.Year; year <= to.Year; year++)
        {
            var start = from > new DateOnly(year, 1, 1) ? from : new DateOnly(year, 1, 1);
            var end = to < new DateOnly(year, 12, 31) ? to : new DateOnly(year, 12, 31);
            result[year] = CountWorkingDays(start, end);
        }

        return result;
    }
}
