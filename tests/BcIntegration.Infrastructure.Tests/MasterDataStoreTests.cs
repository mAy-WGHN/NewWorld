using BcIntegration.Core.MasterData;
using BcIntegration.Infrastructure.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shared.Testing;

namespace BcIntegration.Infrastructure.Tests;

public sealed class MasterDataStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2028, 2, 29, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Internal_id_is_issued_once_per_bc_record_and_entity_type()
    {
        var cs = await BcDatabase.CreateAsync(postgres);
        var bcId = Guid.NewGuid();
        Guid employeeId, again, storeId;
        await using (var db = BcDatabase.Open(cs))
        {
            var store = new EfMasterDataStore(db, _time);
            employeeId = await store.GetOrCreateInternalIdAsync(MasterDataEntity.Employee, bcId, CancellationToken.None);
            storeId = await store.GetOrCreateInternalIdAsync(MasterDataEntity.Store, bcId, CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = BcDatabase.Open(cs))
        {
            again = await new EfMasterDataStore(db, _time).GetOrCreateInternalIdAsync(MasterDataEntity.Employee, bcId, CancellationToken.None);
        }

        again.ShouldBe(employeeId);
        storeId.ShouldNotBe(employeeId);
        employeeId.Version.ShouldBe(7);
    }

    [Fact]
    public async Task Same_bc_record_seen_twice_in_one_run_gets_one_id()
    {
        var cs = await BcDatabase.CreateAsync(postgres);
        await using var db = BcDatabase.Open(cs);
        var store = new EfMasterDataStore(db, _time);
        var bcId = Guid.NewGuid();

        var first = await store.GetOrCreateInternalIdAsync(MasterDataEntity.Employee, bcId, CancellationToken.None);
        var second = await store.GetOrCreateInternalIdAsync(MasterDataEntity.Employee, bcId, CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);

        second.ShouldBe(first);
    }

    [Fact]
    public async Task States_and_watermarks_round_trip()
    {
        var cs = await BcDatabase.CreateAsync(postgres);
        var employeeId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var employee = new EmployeeState("10042", "Erika Mustermann", storeId, "VERKAUF", "TEILZEIT", 30.5m, new DateOnly(2024, 2, 29), null);
        var storeState = new StoreState("F017", "Filiale Musterstadt", new Address("Hauptstraße 1", "12345", "Musterstadt"),
            [new OpeningHours(DayOfWeek.Saturday, new TimeOnly(7, 30), new TimeOnly(18, 0))], true);
        var watermark = new SyncWatermark(new DateTimeOffset(2026, 3, 29, 1, 59, 59, TimeSpan.Zero), true);
        await using (var db = BcDatabase.Open(cs))
        {
            var store = new EfMasterDataStore(db, _time);
            await store.SaveEmployeeAsync(employeeId, employee, CancellationToken.None);
            await store.SaveStoreAsync(storeId, storeState, CancellationToken.None);
            await store.SaveWatermarkAsync(MasterDataEntity.Employee, watermark, CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = BcDatabase.Open(cs))
        {
            var store = new EfMasterDataStore(db, _time);
            (await store.GetEmployeeAsync(employeeId, CancellationToken.None)).ShouldBe(employee);
            var loaded = await store.GetStoreAsync(storeId, CancellationToken.None);
            StoreChangeDetector.HasChanged(loaded, storeState).ShouldBeFalse();
            (await store.FindStoreIdByNumberAsync("F017", CancellationToken.None)).ShouldBe(storeId);
            (await store.GetWatermarkAsync(MasterDataEntity.Employee, CancellationToken.None)).ShouldBe(watermark);
            (await store.GetWatermarkAsync(MasterDataEntity.Store, CancellationToken.None)).ShouldBe(SyncWatermark.None);
        }
    }

    [Fact]
    public async Task Updated_state_overwrites_the_previous_one()
    {
        var cs = await BcDatabase.CreateAsync(postgres);
        var employeeId = Guid.NewGuid();
        await using (var db = BcDatabase.Open(cs))
        {
            var store = new EfMasterDataStore(db, _time);
            await store.SaveEmployeeAsync(employeeId, new EmployeeState("1", "A", null, null, null, null, null, null), CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = BcDatabase.Open(cs))
        {
            var store = new EfMasterDataStore(db, _time);
            await store.SaveEmployeeAsync(employeeId, new EmployeeState("1", "B", null, null, null, null, null, new DateOnly(2026, 12, 31)), CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
            (await store.GetEmployeeAsync(employeeId, CancellationToken.None))!.Name.ShouldBe("B");
        }
    }

    [Fact]
    public async Task Erasure_deletes_personal_data_and_is_remembered()
    {
        var cs = await BcDatabase.CreateAsync(postgres);
        var employeeId = Guid.NewGuid();
        await using (var db = BcDatabase.Open(cs))
        {
            var store = new EfMasterDataStore(db, _time);
            await store.SaveEmployeeAsync(employeeId, new EmployeeState("10042", "Erika", null, null, null, null, null, null), CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
            await store.MarkErasedAsync(employeeId, CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = BcDatabase.Open(cs))
        {
            var store = new EfMasterDataStore(db, _time);
            (await store.GetEmployeeAsync(employeeId, CancellationToken.None)).ShouldBeNull();
            (await store.IsErasedAsync(employeeId, CancellationToken.None)).ShouldBeTrue();
            (await db.EmployeeStates.CountAsync()).ShouldBe(0);
        }
    }
}
