using Portal.Core.Logbook;

namespace Portal.Core.CashRegisters;

/// <summary>
/// The daily reminder for cash registers that are not sealed: on or off and the local time of day from which it goes out (once per local day). It tells
/// the store managers of the store and always the district leaders of the district (what is waiting, no details). TODO(Fachlogik): the meaning of "sealed"
/// and the right time are assumptions until confirmed; HR or the district leaders can change both here.
/// </summary>
public sealed record CashRegisterReminderSettings(bool Enabled, TimeOnly Time)
{
    public static readonly CashRegisterReminderSettings Default = new(true, new TimeOnly(18, 0));
}

public static class CashRegisterReminderPlan
{
    /// <summary>
    /// The local day to remind for, or <c>null</c> if nothing is due: the reminder is on, the local time is at or past the time of the settings and
    /// that local day was not reminded yet. A day is the local calendar day (summer and winter time, leap day and the change of the year just work).
    /// </summary>
    public static DateOnly? DueDay(CashRegisterReminderSettings settings, DateTimeOffset now, TimeZoneInfo zone, DateOnly? lastReminded)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var day = DateOnly.FromDateTime(local);
        if (TimeOnly.FromDateTime(local) < settings.Time || (lastReminded is { } last && last >= day))
        {
            return null;
        }

        return day;
    }
}

/// <summary>"Always inform and show": the stores of the districts a person leads (all regions in them and the stores directly in the district).</summary>
public static class CashRegisterReach
{
    public static IReadOnlySet<Guid> DistrictStores(LogbookStructure structure, Guid person)
    {
        var districts = structure.Districts.Where(d => d.LeaderIds.Contains(person)).Select(d => d.Id).ToHashSet();
        return structure.Stores.Where(s => districts.Contains(s.DistrictId)).Select(s => s.StoreId).ToHashSet();
    }
}
