using BcIntegration.Contracts;
using BcIntegration.Core.MasterData;
using BcIntegration.Core.Messaging;
using BcIntegration.Core.Workforce;
using Shared.Messaging;
using Workforce.Contracts;

namespace BcIntegration.Core.Tests;

internal sealed class FakeMasterDataSource : IBcMasterDataSource
{
    public List<BcStore> Stores { get; } = [];
    public List<BcEmployee> Employees { get; } = [];
    public List<DateTimeOffset?> EmployeeQueries { get; } = [];

    public Task<IReadOnlyList<BcStore>> GetStoresModifiedSinceAsync(DateTimeOffset? since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BcStore>>(Stores.Where(s => since is null || s.LastModified > since).ToList());

    public Task<IReadOnlyList<BcEmployee>> GetEmployeesModifiedSinceAsync(DateTimeOffset? since, CancellationToken cancellationToken)
    {
        EmployeeQueries.Add(since);
        return Task.FromResult<IReadOnlyList<BcEmployee>>(Employees.Where(e => since is null || e.LastModified > since).ToList());
    }
}

internal sealed class FakeMasterDataStore : IMasterDataStore
{
    public Dictionary<(MasterDataEntity, Guid), Guid> Ids { get; } = [];
    public Dictionary<Guid, EmployeeState> Employees { get; } = [];
    public HashSet<Guid> Erased { get; } = [];
    public Dictionary<Guid, StoreState> Stores { get; } = [];
    public Dictionary<MasterDataEntity, SyncWatermark> Watermarks { get; } = [];
    public int Saves { get; private set; }

    public Task<Guid> GetOrCreateInternalIdAsync(MasterDataEntity entity, Guid bcSystemId, CancellationToken cancellationToken)
    {
        if (!Ids.TryGetValue((entity, bcSystemId), out var id))
        {
            id = Guid.NewGuid();
            Ids[(entity, bcSystemId)] = id;
        }

        return Task.FromResult(id);
    }

    public Task<EmployeeState?> GetEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(Employees.GetValueOrDefault(employeeId));

    public Task SaveEmployeeAsync(Guid employeeId, EmployeeState state, CancellationToken cancellationToken)
    {
        Employees[employeeId] = state;
        return Task.CompletedTask;
    }

    public Task<bool> IsErasedAsync(Guid employeeId, CancellationToken cancellationToken) => Task.FromResult(Erased.Contains(employeeId));

    public Task MarkErasedAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        Employees.Remove(employeeId);
        Erased.Add(employeeId);
        return Task.CompletedTask;
    }

    public Task<StoreState?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken) => Task.FromResult(Stores.GetValueOrDefault(storeId));

    public Task SaveStoreAsync(Guid storeId, StoreState state, CancellationToken cancellationToken)
    {
        Stores[storeId] = state;
        return Task.CompletedTask;
    }

    public Task<Guid?> FindStoreIdByNumberAsync(string storeNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Stores.Where(s => s.Value.StoreNumber == storeNumber).Select(s => (Guid?)s.Key).FirstOrDefault());

    public Task<SyncWatermark> GetWatermarkAsync(MasterDataEntity entity, CancellationToken cancellationToken) =>
        Task.FromResult(Watermarks.GetValueOrDefault(entity, SyncWatermark.None));

    public Task SaveWatermarkAsync(MasterDataEntity entity, SyncWatermark watermark, CancellationToken cancellationToken)
    {
        Watermarks[entity] = watermark;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingPublisher : IEventPublisher
{
    public List<IEvent> Events { get; } = [];

    public void Publish(IEvent message) => Events.Add(message);

    public IEnumerable<T> Of<T>() => Events.OfType<T>();
}

internal sealed class FakeAbsenceWriter : IBcAbsenceWriter
{
    public List<SyncAbsenceToBc> Calls { get; } = [];
    public Func<SyncAbsenceToBc, BcWriteResult> Result { get; set; } = _ => new BcWriteResult.Succeeded(Guid.Parse("8f2c1e4a-5b6d-4c3e-9a1b-2c3d4e5f6a7b"));

    public Task<BcWriteResult> UpsertAsync(SyncAbsenceToBc absence, CancellationToken cancellationToken)
    {
        Calls.Add(absence);
        return Task.FromResult(Result(absence));
    }
}

internal sealed class FakeAbsenceSyncStore : IAbsenceSyncStore
{
    public Dictionary<Guid, AbsenceSyncState> States { get; } = [];
    public List<SyncError> Errors { get; } = [];

    public Task<AbsenceSyncState?> GetAsync(Guid absenceId, CancellationToken cancellationToken) => Task.FromResult(States.GetValueOrDefault(absenceId));

    public Task SaveAsync(AbsenceSyncState state, CancellationToken cancellationToken)
    {
        States[state.AbsenceId] = state;
        return Task.CompletedTask;
    }

    public Task AddErrorAsync(SyncError error, CancellationToken cancellationToken)
    {
        Errors.Add(error);
        return Task.CompletedTask;
    }
}
