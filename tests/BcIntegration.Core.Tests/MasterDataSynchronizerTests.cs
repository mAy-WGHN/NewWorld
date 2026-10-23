using BcIntegration.Contracts;
using BcIntegration.Core.MasterData;
using Microsoft.Extensions.Time.Testing;

namespace BcIntegration.Core.Tests;

public class MasterDataSynchronizerTests
{
    private static readonly Guid BcStore = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid BcEmployee = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private readonly FakeMasterDataSource _source = new();
    private readonly FakeMasterDataStore _store = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero));

    private MasterDataSynchronizer Synchronizer() => new(_source, _store, _publisher, _time, new MasterDataSyncOptions());

    private static DateTimeOffset At(int minute) => new(2026, 12, 31, 22, minute, 0, TimeSpan.Zero);

    private static BcStore Store(int modifiedMinute, string name = "Filiale Musterstadt") =>
        new(BcStore, "F017", name, new Address("Hauptstraße 1", "12345", "Musterstadt"), [], IsActive: true, At(modifiedMinute));

    private static BcEmployee Employee(int modifiedMinute, DateOnly? exit = null, string name = "Erika Mustermann") =>
        new(BcEmployee, "10042", name, "F017", "VERKAUF", "TEILZEIT", 30m, new DateOnly(2024, 2, 29), exit, At(modifiedMinute));

    [Fact]
    public async Task Initial_load_publishes_state_but_no_lifecycle_events()
    {
        _source.Stores.Add(Store(1));
        _source.Employees.Add(Employee(2, exit: new DateOnly(2025, 6, 30)));

        await Synchronizer().SyncAsync(CancellationToken.None);

        _publisher.Of<StoreChanged>().ShouldHaveSingleItem();
        var changed = _publisher.Of<EmployeeChanged>().ShouldHaveSingleItem();
        _publisher.Of<EmployeeHired>().ShouldBeEmpty();
        _publisher.Of<EmployeeLeft>().ShouldBeEmpty();
        changed.HomeStoreId.ShouldBe(_publisher.Of<StoreChanged>().Single().StoreId);
        changed.PersonnelNumber.ShouldBe("10042");
        _store.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Unchanged_data_on_the_next_run_publishes_nothing()
    {
        _source.Stores.Add(Store(1));
        _source.Employees.Add(Employee(2));
        await Synchronizer().SyncAsync(CancellationToken.None);
        _publisher.Events.Clear();

        // BC touched the record (new lastModifiedDateTime) without changing relevant fields.
        _source.Employees[0] = Employee(5);
        await Synchronizer().SyncAsync(CancellationToken.None);

        _publisher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exit_date_after_the_initial_load_publishes_changed_and_left()
    {
        _source.Employees.Add(Employee(2));
        await Synchronizer().SyncAsync(CancellationToken.None);
        _publisher.Events.Clear();

        _source.Employees[0] = Employee(10, exit: new DateOnly(2026, 12, 31));
        await Synchronizer().SyncAsync(CancellationToken.None);

        _publisher.Of<EmployeeChanged>().ShouldHaveSingleItem().ExitDate.ShouldBe(new DateOnly(2026, 12, 31));
        _publisher.Of<EmployeeLeft>().ShouldHaveSingleItem().ExitDate.ShouldBe(new DateOnly(2026, 12, 31));
        _publisher.Events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task New_employee_after_the_initial_load_is_hired()
    {
        _source.Stores.Add(Store(1));
        await Synchronizer().SyncAsync(CancellationToken.None);
        _publisher.Events.Clear();

        _source.Employees.Add(Employee(20));
        await Synchronizer().SyncAsync(CancellationToken.None);

        var hired = _publisher.Of<EmployeeHired>().ShouldHaveSingleItem();
        hired.EntryDate.ShouldBe(new DateOnly(2024, 2, 29));
        hired.HomeStoreId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Next_run_asks_only_for_changes_since_the_watermark_minus_overlap()
    {
        _source.Employees.Add(Employee(2));
        await Synchronizer().SyncAsync(CancellationToken.None);
        await Synchronizer().SyncAsync(CancellationToken.None);

        _source.EmployeeQueries.ShouldBe([null, At(2) - new MasterDataSyncOptions().Overlap]);
    }

    [Fact]
    public async Task Same_bc_record_keeps_its_internal_id_across_runs()
    {
        _source.Employees.Add(Employee(2));
        await Synchronizer().SyncAsync(CancellationToken.None);
        _source.Employees[0] = Employee(30, name: "Erika Musterfrau");
        await Synchronizer().SyncAsync(CancellationToken.None);

        _publisher.Of<EmployeeChanged>().Select(e => e.EmployeeId).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Unknown_store_number_leaves_the_home_store_empty()
    {
        _source.Employees.Add(Employee(2) with { StoreNumber = "F999" });

        await Synchronizer().SyncAsync(CancellationToken.None);

        _publisher.Of<EmployeeChanged>().Single().HomeStoreId.ShouldBeNull();
    }

    [Fact]
    public async Task Erased_employee_is_not_copied_again_by_later_runs()
    {
        _source.Employees.Add(Employee(2));
        await Synchronizer().SyncAsync(CancellationToken.None);
        var employeeId = _publisher.Of<EmployeeChanged>().Single().EmployeeId;
        _publisher.Events.Clear();

        (await new EmployeeErasure(_store, _publisher, _time).RequestAsync(employeeId, CancellationToken.None)).ShouldBeTrue();
        _source.Employees[0] = Employee(40, name: "Erika Musterfrau");
        await Synchronizer().SyncAsync(CancellationToken.None);

        _publisher.Of<EmployeeDataErasureRequested>().ShouldHaveSingleItem().EmployeeId.ShouldBe(employeeId);
        _publisher.Of<EmployeeChanged>().ShouldBeEmpty();
        _store.Employees.ShouldNotContainKey(employeeId);
    }

    [Fact]
    public async Task Erasure_of_an_unknown_employee_is_refused()
    {
        (await new EmployeeErasure(_store, _publisher, _time).RequestAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();
        _publisher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Messages_of_one_run_share_a_correlation_id_and_use_the_clock()
    {
        _source.Stores.Add(Store(1));
        _source.Employees.Add(Employee(2));

        await Synchronizer().SyncAsync(CancellationToken.None);

        _publisher.Events.Select(e => e.CorrelationId).Distinct().ShouldHaveSingleItem();
        _publisher.Events.ShouldAllBe(e => e.OccurredAt == _time.GetUtcNow());
        _publisher.Events.Select(e => e.MessageId).Distinct().Count().ShouldBe(_publisher.Events.Count);
    }
}
