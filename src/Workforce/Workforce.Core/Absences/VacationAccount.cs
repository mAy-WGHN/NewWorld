using Workforce.Contracts;

namespace Workforce.Core.Absences;

/// <summary>A vacation of a person (bounded period) with its status.</summary>
public sealed record VacationBooking(DateRange Range, AbsenceStatus Status);

/// <summary>Vacation account of a year. <see cref="Remaining"/> can become negative if the entitlement is lowered afterwards.</summary>
public sealed record VacationBalance(int Year, decimal Entitlement, decimal Taken, decimal Planned, decimal Remaining);

/// <summary>
/// Entitlement per year (maintained manually, TODO(BC-Modell): later import), taken, planned and remaining days. Approved days before
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
}
