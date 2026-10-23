using BcIntegration.Contracts;
using BcIntegration.Core.Messaging;

namespace BcIntegration.Core.MasterData;

public sealed record MasterDataSyncResult(int StoresChanged, int EmployeesChanged, int Hired, int Left);

/// <summary>
/// One comparison run: reads records changed since the watermark, compares them with the last published state and
/// publishes only real changes (stores first, so employees can reference their home store). Everything of one run
/// commits in one transaction. Callable by the poller and later by a BC webhook.
/// </summary>
public sealed class MasterDataSynchronizer(
    IBcMasterDataSource source,
    IMasterDataStore store,
    IEventPublisher publisher,
    TimeProvider time,
    MasterDataSyncOptions options)
{
    public async Task<MasterDataSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var correlationId = MessageIds.New(time);
        var storesChanged = await SyncStoresAsync(correlationId, cancellationToken);
        var (employeesChanged, hired, left) = await SyncEmployeesAsync(correlationId, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return new MasterDataSyncResult(storesChanged, employeesChanged, hired, left);
    }

    private async Task<int> SyncStoresAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var watermark = await store.GetWatermarkAsync(MasterDataEntity.Store, cancellationToken);
        var records = await source.GetStoresModifiedSinceAsync(Since(watermark), cancellationToken);
        var changed = 0;
        foreach (var record in records)
        {
            var storeId = await store.GetOrCreateInternalIdAsync(MasterDataEntity.Store, record.BcSystemId, cancellationToken);
            var current = new StoreState(record.StoreNumber, record.Name, record.Address, record.OpeningHours, record.IsActive);
            if (!StoreChangeDetector.HasChanged(await store.GetStoreAsync(storeId, cancellationToken), current))
            {
                continue;
            }

            await store.SaveStoreAsync(storeId, current, cancellationToken);
            publisher.Publish(new StoreChanged(
                MessageIds.New(time), correlationId, time.GetUtcNow(), storeId, current.StoreNumber, current.Name,
                current.Address is { } a ? new StoreAddress(a.Street, a.PostalCode, a.City) : null,
                current.OpeningHours.Select(h => new StoreOpeningHours(ToWeekday(h.Day), h.Opens, h.Closes)).ToList(),
                current.IsActive));
            changed++;
        }

        await store.SaveWatermarkAsync(MasterDataEntity.Store, Advance(watermark, records.Select(r => r.LastModified)), cancellationToken);
        return changed;
    }

    private async Task<(int Changed, int Hired, int Left)> SyncEmployeesAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var watermark = await store.GetWatermarkAsync(MasterDataEntity.Employee, cancellationToken);
        var records = await source.GetEmployeesModifiedSinceAsync(Since(watermark), cancellationToken);
        int changed = 0, hired = 0, left = 0;
        foreach (var record in records)
        {
            var employeeId = await store.GetOrCreateInternalIdAsync(MasterDataEntity.Employee, record.BcSystemId, cancellationToken);
            if (await store.IsErasedAsync(employeeId, cancellationToken))
            {
                continue;
            }

            var homeStoreId = record.StoreNumber is null ? null : await store.FindStoreIdByNumberAsync(record.StoreNumber, cancellationToken);
            var current = new EmployeeState(
                record.PersonnelNumber, record.Name, homeStoreId, record.Position, record.EmploymentType,
                record.ContractedHoursPerWeek, record.EntryDate, record.ExitDate);
            var changes = EmployeeChangeDetector.Detect(
                await store.GetEmployeeAsync(employeeId, cancellationToken), current, lifecycleEvents: watermark.InitialLoadCompleted);
            if (!changes.Changed)
            {
                continue;
            }

            await store.SaveEmployeeAsync(employeeId, current, cancellationToken);
            var now = time.GetUtcNow();
            publisher.Publish(new EmployeeChanged(
                MessageIds.New(time), correlationId, now, employeeId, current.PersonnelNumber, current.Name, current.HomeStoreId,
                current.Position, current.EmploymentType, current.ContractedHoursPerWeek, current.EntryDate, current.ExitDate));
            changed++;
            if (changes.Hired)
            {
                publisher.Publish(new EmployeeHired(MessageIds.New(time), correlationId, now, employeeId, current.EntryDate!.Value, current.HomeStoreId));
                hired++;
            }

            if (changes.Left)
            {
                publisher.Publish(new EmployeeLeft(MessageIds.New(time), correlationId, now, employeeId, current.ExitDate!.Value));
                left++;
            }
        }

        await store.SaveWatermarkAsync(MasterDataEntity.Employee, Advance(watermark, records.Select(r => r.LastModified)), cancellationToken);
        return (changed, hired, left);
    }

    private DateTimeOffset? Since(SyncWatermark watermark) => watermark.LastModified - options.Overlap;

    private static SyncWatermark Advance(SyncWatermark watermark, IEnumerable<DateTimeOffset> modified)
    {
        var latest = modified.Append(watermark.LastModified ?? DateTimeOffset.MinValue).Max();
        return new SyncWatermark(latest == DateTimeOffset.MinValue ? null : latest, InitialLoadCompleted: true);
    }

    private static Weekday ToWeekday(DayOfWeek day) => day == DayOfWeek.Sunday ? Weekday.Sunday : (Weekday)(int)day;
}
