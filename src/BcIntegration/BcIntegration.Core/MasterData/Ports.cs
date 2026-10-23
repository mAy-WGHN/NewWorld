namespace BcIntegration.Core.MasterData;

public enum MasterDataEntity
{
    Unknown = 0,
    Employee = 1,
    Store = 2,
}

/// <summary>Progress of the change detection per entity type.</summary>
/// <param name="LastModified">Highest <c>lastModifiedDateTime</c> processed so far.</param>
/// <param name="InitialLoadCompleted">After the first complete run, hired/left events are derived.</param>
public sealed record SyncWatermark(DateTimeOffset? LastModified, bool InitialLoadCompleted)
{
    public static readonly SyncWatermark None = new(null, false);
}

/// <summary>Reads master data from Business Central (implemented by the BC client).</summary>
public interface IBcMasterDataSource
{
    /// <summary>Stores changed after <paramref name="since"/>; all stores if <c>null</c>.</summary>
    Task<IReadOnlyList<BcStore>> GetStoresModifiedSinceAsync(DateTimeOffset? since, CancellationToken cancellationToken);

    /// <summary>Employees changed after <paramref name="since"/>; all employees if <c>null</c>.</summary>
    Task<IReadOnlyList<BcEmployee>> GetEmployeesModifiedSinceAsync(DateTimeOffset? since, CancellationToken cancellationToken);
}

/// <summary>BC Integration's own data: ID mapping, last published states, watermarks, erasure markers.</summary>
public interface IMasterDataStore
{
    /// <summary>The system-wide internal ID for a BC record; issued on first sight.</summary>
    Task<Guid> GetOrCreateInternalIdAsync(MasterDataEntity entity, Guid bcSystemId, CancellationToken cancellationToken);

    Task<EmployeeState?> GetEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    Task SaveEmployeeAsync(Guid employeeId, EmployeeState state, CancellationToken cancellationToken);

    Task<bool> IsErasedAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>Deletes the stored personal data and remembers that the person must not be copied again.</summary>
    Task MarkErasedAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<StoreState?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken);

    Task SaveStoreAsync(Guid storeId, StoreState state, CancellationToken cancellationToken);

    Task<Guid?> FindStoreIdByNumberAsync(string storeNumber, CancellationToken cancellationToken);

    Task<SyncWatermark> GetWatermarkAsync(MasterDataEntity entity, CancellationToken cancellationToken);

    Task SaveWatermarkAsync(MasterDataEntity entity, SyncWatermark watermark, CancellationToken cancellationToken);

    /// <summary>Commits states, watermarks and published events in one transaction.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
