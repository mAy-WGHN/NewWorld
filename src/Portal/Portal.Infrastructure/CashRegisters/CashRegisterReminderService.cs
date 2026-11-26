using Microsoft.EntityFrameworkCore;
using Portal.Core.CashRegisters;
using Portal.Core.Logbook;
using Portal.Infrastructure.Logbook;

namespace Portal.Infrastructure.CashRegisters;

/// <summary>
/// The daily reminder for cash registers that are not sealed: once per local day from the time of the settings. The store managers of a store with such
/// a register are told, and <b>always the district leaders</b> of the district (one message per district, whatever the privileges of the leader). The
/// settings are kept in one row; without it the default applies (on, 18:00).
/// </summary>
public sealed class CashRegisterReminderService(PortalDbContext db, LogbookStructureStore structures, CashRegisterNotifier notifier, TimeProvider time, LogbookOptions options)
{
    public async Task<CashRegisterReminderSettings> GetSettingsAsync(CancellationToken cancellationToken) =>
        await db.CashRegisterReminderSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) is { } row
            ? new CashRegisterReminderSettings(row.Enabled, row.Time)
            : CashRegisterReminderSettings.Default;

    public async Task<CashRegisterReminderSettings> SaveSettingsAsync(CashRegisterReminderSettings wanted, Guid by, CancellationToken cancellationToken)
    {
        var row = await db.CashRegisterReminderSettings.FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new CashRegisterReminderSettingsRecord();
            db.CashRegisterReminderSettings.Add(row);
        }

        row.Enabled = wanted.Enabled;
        row.Time = wanted.Time;
        row.ChangedAt = time.GetUtcNow();
        row.ChangedBy = by == Guid.Empty ? null : by;
        await db.SaveChangesAsync(cancellationToken);
        return wanted;
    }

    /// <summary>One round of the background service. Returns how many notifications went out (0 if nothing was due or nobody is to be told).</summary>
    public async Task<int> RunAutomaticAsync(CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var last = await db.CashRegisterReminders.AsNoTracking().OrderByDescending(r => r.Day).Select(r => (DateOnly?)r.Day).FirstOrDefaultAsync(cancellationToken);
        if (CashRegisterReminderPlan.DueDay(settings, time.GetUtcNow(), options.Zone, last) is not { } day)
        {
            return 0;
        }

        var unsealedStores = await db.CashRegisters.AsNoTracking().Where(r => !r.IsSealed).Select(r => r.StoreId).Distinct().ToListAsync(cancellationToken);
        var structure = await structures.LoadAsync(cancellationToken);
        var correlation = Guid.CreateVersion7();
        var sent = 0;

        foreach (var storeId in unsealedStores)
        {
            if (notifier.Send(CashRegisterNotifier.UnsealedStore, $"{storeId}:{day:yyyy-MM-dd}", notifier.StoreManagersOf(storeId), correlation))
            {
                sent++;
            }
        }

        // the district leaders are always told, once per district with at least one such store
        var districts = structure.Stores.Where(s => unsealedStores.Contains(s.StoreId)).Select(s => s.DistrictId).Distinct().ToList();
        foreach (var district in structure.Districts.Where(d => districts.Contains(d.Id)))
        {
            if (notifier.Send(CashRegisterNotifier.UnsealedDistrict, $"{district.Id}:{day:yyyy-MM-dd}", district.LeaderIds, correlation, quiet: true))
            {
                sent++;
            }
        }

        if (unsealedStores.Count > 0 && sent == 0)
        {
            return 0; // there is something to remind but nobody to tell: nothing is marked, the next round looks again
        }

        db.CashRegisterReminders.Add(new CashRegisterReminderRecord { Day = day, SentAt = time.GetUtcNow(), Notifications = sent });
        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }
}
