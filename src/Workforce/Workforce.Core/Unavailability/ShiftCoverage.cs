using Notifications.Contracts;
using Workforce.Contracts;
using Workforce.Core.Absences;

namespace Workforce.Core.Unavailability;

public enum UncoveredReason
{
    Unknown = 0,
    Sickness = 1,
    Absence = 2,
}

/// <summary>
/// When a person becomes unavailable on a day with a planned shift, the shift has nobody: <see cref="ShiftUncovered"/> is published (neutralised, no
/// person, no reason) and the persons in HR responsible for the schedule (topic <c>schedule.adjustment</c> in Access, maintained there) get one
/// in-app notice per store and month. The notice contains class 2 information (sickness), so in-app is its only channel, and neither person nor
/// kind of absence appear in it.
/// </summary>
public sealed class ShiftCoverage(IShiftLookup shifts, IResponsibilityDirectory responsibilities, IWorkforceMessages messages, TimeProvider time, WorkforceOptions options)
{
    private static readonly string[] Months =
        ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    public static string TemplateKey(UncoveredReason reason) =>
        reason == UncoveredReason.Sickness ? "workforce.shift-uncovered-sickness.hr" : "workforce.shift-uncovered-absence.hr";

    /// <param name="employeeId">The person who is unavailable.</param>
    /// <param name="range">The days of the unavailability.</param>
    /// <param name="reason">Sickness or another absence; chooses the text.</param>
    /// <param name="correlationId">Correlates the messages of one flow.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="sourceId">The absence or entry that made the person unavailable (part of the notification ID).</param>
    /// <param name="version">Version of the source; a changed period is a new notice.</param>
    public async Task ReportAsync(
        Guid employeeId, DateRange range, UncoveredReason reason, Guid sourceId, Guid correlationId, CancellationToken cancellationToken, long version = 1)
    {
        var planned = await shifts.PlannedShiftsAsync(employeeId, range, cancellationToken);
        if (planned.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var shift in planned)
        {
            messages.Publish(new ShiftUncovered(Guid.CreateVersion7(now), correlationId, now, shift.ShiftId, shift.StoreId, shift.From, shift.To));
        }

        var recipients = (await responsibilities.ScheduleAdjustmentHandlersAsync(employeeId, cancellationToken)).Where(r => r != employeeId).Distinct().ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var templateKey = TemplateKey(reason);
        foreach (var group in planned.GroupBy(s =>
        {
            var local = TimeZoneInfo.ConvertTime(s.From, options.Zone);
            return (s.StoreId, s.StoreLabel, local.Year, local.Month);
        }))
        {
            var (storeId, label, year, month) = group.Key;
            messages.Send(new SendNotification(
                Guid.CreateVersion7(now), correlationId, now,
                DeterministicGuid.From($"{sourceId}:{version}:{storeId}:{year}-{month}:{templateKey}"),
                recipients, templateKey,
                new Dictionary<string, string> { ["DP.store"] = label, ["DP.month"] = Months[month - 1], ["DP.year"] = year.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                [NotificationChannel.InApp], $"/stores/{storeId}/schedule/{year}-{month:00}"));
        }
    }
}
