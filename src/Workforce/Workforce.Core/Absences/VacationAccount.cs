using Workforce.Contracts;

namespace Workforce.Core.Absences;

/// <summary>A vacation of a person (bounded period) with its status.</summary>
public sealed record VacationBooking(DateRange Range, AbsenceStatus Status);

/// <summary>Vacation account of a year. <see cref="Remaining"/> can become negative if the entitlement is lowered afterwards.</summary>
public sealed record VacationBalance(int Year, decimal Entitlement, decimal Taken, decimal Planned, decimal Remaining);

/// <summary>
/// Entitlement per year (maintained by HR, decision 24), taken, planned and remaining days. Approved days before
/// today are taken, approved days from today on and requested days are planned (requested days are reserved so a second request cannot
/// exceed the account). Rejected, withdrawn and cancelled vacation does not count (credited).
/// </summary>
public static class VacationAccount
{
    public static VacationBalance Compute(int year, decimal entitlement, IEnumerable<VacationBooking> bookings, DateOnly today, WorkCalendar calendar)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        decimal taken = 0;
        decimal planned = 0;
        foreach (var booking in bookings)
        {
            if (booking.Status is not (AbsenceStatus.Approved or AbsenceStatus.Requested) || booking.Range.To is not { } to)
            {
                continue;
            }

            var from = booking.Range.From > yearStart ? booking.Range.From : yearStart;
            var end = to < yearEnd ? to : yearEnd;
            if (end < from)
            {
                continue;
            }

            if (booking.Status == AbsenceStatus.Requested || from >= today)
            {
                planned += calendar.CountWorkingDays(from, end);
                continue;
            }

            var lastTakenDay = end < today.AddDays(-1) ? end : today.AddDays(-1);
            taken += calendar.CountWorkingDays(from, lastTakenDay);
            if (end >= today)
            {
                planned += calendar.CountWorkingDays(today, end);
            }
        }

        return new VacationBalance(year, entitlement, taken, planned, entitlement - taken - planned);
    }

    /// <summary>
    /// The account of a year with contractual and additional days. Vacation days are taken from the additional days first (the ones that
    /// expire earlier first), then from the contractual days of the year. Additional days that are not used by their expiry date are lost.
    /// </summary>
    /// <param name="year">The year of the account.</param>
    /// <param name="contractualAnnualDays">Days per year according to the contract.</param>
    /// <param name="entryDate">Date of joining, <c>null</c> if unknown; in the year of joining the days are pro rata.</param>
    /// <param name="extras">Additional days of the person (all years; the ones outside the year are ignored).</param>
    /// <param name="rules">When additional days expire (by default never).</param>
    /// <param name="bookings">All vacation bookings of the person.</param>
    /// <param name="today">Today: approved days before it are taken, from it on planned.</param>
    /// <param name="asOf">
    /// The date the account is evaluated for: additional days that have expired before it count as lost. Today for a display; the end of a
    /// requested period when a request is checked (days that expire before it cannot cover it).
    /// </param>
    /// <param name="calendar">Working days of the person.</param>
    /// <param name="changes">Changes of the annual entitlement during the year (other working days per week etc.).</param>
    public static VacationBreakdown Compute(
        int year,
        decimal contractualAnnualDays,
        DateOnly? entryDate,
        IReadOnlyList<ExtraVacationDays> extras,
        VacationRules rules,
        IEnumerable<VacationBooking> bookings,
        DateOnly today,
        DateOnly asOf,
        WorkCalendar calendar,
        IReadOnlyList<EntitlementChange>? changes = null)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var contractual = VacationEntitlement.Contractual(contractualAnnualDays, entryDate, year, rules, changes);

        // Additional days whose usable window touches the year, the earlier expiring ones first.
        var relevant = extras
            .Where(e => e.Days > 0 && e.UsableFrom <= yearEnd && (e.ExpiresOn(rules) is not { } expires || expires >= yearStart))
            .OrderBy(e => e.ExpiresOn(rules) ?? DateOnly.MaxValue).ThenBy(e => e.UsableFrom).ThenBy(e => e.Id)
            .ToList();
        var simulationStart = relevant.Count == 0 ? yearStart : relevant.Min(e => e.UsableFrom) < yearStart ? relevant.Min(e => e.UsableFrom) : yearStart;

        var days = new List<(DateOnly Day, AbsenceStatus Status)>();
        foreach (var booking in bookings)
        {
            if (booking.Status is not (AbsenceStatus.Approved or AbsenceStatus.Requested) || booking.Range.To is not { } to)
            {
                continue;
            }

            for (var day = booking.Range.From > simulationStart ? booking.Range.From : simulationStart; day <= to && day <= yearEnd; day = day.AddDays(1))
            {
                if (calendar.IsWorkingDay(day))
                {
                    days.Add((day, booking.Status));
                }
            }
        }

        var left = relevant.ToDictionary(e => e.Id, e => e.Days);
        var usedBefore = relevant.ToDictionary(e => e.Id, _ => 0m);
        var usedInYear = relevant.ToDictionary(e => e.Id, _ => 0m);
        decimal taken = 0;
        decimal planned = 0;
        foreach (var (day, status) in days.OrderBy(d => d.Day))
        {
            var need = 1m;
            foreach (var extra in relevant.Where(e => e.UsableFrom <= day && (e.ExpiresOn(rules) is not { } expires || day <= expires) && left[e.Id] > 0))
            {
                var part = Math.Min(need, left[extra.Id]);
                left[extra.Id] -= part;
                need -= part;
                if (day < yearStart)
                {
                    usedBefore[extra.Id] += part;
                }
                else
                {
                    usedInYear[extra.Id] += part;
                }

                if (need == 0)
                {
                    break;
                }
            }

            // Whatever the additional days do not cover, and all days of the year are counted: before the year only the use of
            // additional days matters (those days were paid from the contract of their own year).
            if (day < yearStart)
            {
                continue;
            }

            if (status == AbsenceStatus.Approved && day < today)
            {
                taken += 1m;
            }
            else
            {
                planned += 1m;
            }
        }

        var lines = relevant.Select(e =>
        {
            var available = e.Days - usedBefore[e.Id];
            var expiresOn = e.ExpiresOn(rules);
            var forfeited = expiresOn is { } last && last < asOf ? Math.Max(0m, available - usedInYear[e.Id]) : 0m;
            return new ExtraDaysLine(e.Id, e.Kind, e.Year, e.Days, available, usedInYear[e.Id], forfeited, expiresOn);
        }).ToList();

        var extraDays = lines.Sum(l => l.AvailableInYear - l.Forfeited);
        var effective = contractual + extraDays;
        return new VacationBreakdown(
            year, contractualAnnualDays, contractual, extraDays, lines.Sum(l => l.Forfeited), effective, taken, planned, effective - taken - planned, lines);
    }
}
