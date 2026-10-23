using System.Text.Json;
using BcIntegration.Core.MasterData;
using Microsoft.EntityFrameworkCore;

namespace BcIntegration.Infrastructure.MasterData;

public sealed class EfMasterDataStore(BcIntegrationDbContext db, TimeProvider time) : IMasterDataStore
{
    public async Task<Guid> GetOrCreateInternalIdAsync(MasterDataEntity entity, Guid bcSystemId, CancellationToken cancellationToken)
    {
        var mapping = await db.IdMappings.FindAsync([entity.ToString(), bcSystemId], cancellationToken);
        if (mapping is null)
        {
            mapping = new IdMapping
            {
                EntityType = entity.ToString(),
                BcSystemId = bcSystemId,
                InternalId = Guid.CreateVersion7(time.GetUtcNow()),
                CreatedAt = time.GetUtcNow(),
            };
            db.IdMappings.Add(mapping);
        }

        return mapping.InternalId;
    }

    public async Task<EmployeeState?> GetEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.EmployeeStates.FindAsync([employeeId], cancellationToken) is { } r
            ? new EmployeeState(r.PersonnelNumber, r.Name, r.HomeStoreId, r.Position, r.EmploymentType, r.ContractedHoursPerWeek, r.EntryDate, r.ExitDate)
            : null;

    public async Task SaveEmployeeAsync(Guid employeeId, EmployeeState state, CancellationToken cancellationToken)
    {
        var record = await db.EmployeeStates.FindAsync([employeeId], cancellationToken);
        if (record is null)
        {
            record = new EmployeeStateRecord { EmployeeId = employeeId, PersonnelNumber = state.PersonnelNumber, Name = state.Name };
            db.EmployeeStates.Add(record);
        }

        record.PersonnelNumber = state.PersonnelNumber;
        record.Name = state.Name;
        record.HomeStoreId = state.HomeStoreId;
        record.Position = state.Position;
        record.EmploymentType = state.EmploymentType;
        record.ContractedHoursPerWeek = state.ContractedHoursPerWeek;
        record.EntryDate = state.EntryDate;
        record.ExitDate = state.ExitDate;
    }

    public async Task<bool> IsErasedAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.ErasedEmployees.FindAsync([employeeId], cancellationToken) is not null;

    public async Task MarkErasedAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (await db.EmployeeStates.FindAsync([employeeId], cancellationToken) is { } record)
        {
            db.EmployeeStates.Remove(record);
        }

        if (!await IsErasedAsync(employeeId, cancellationToken))
        {
            db.ErasedEmployees.Add(new ErasedEmployee { EmployeeId = employeeId, ErasedAt = time.GetUtcNow() });
        }
    }

    public async Task<StoreState?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken) =>
        await db.StoreStates.FindAsync([storeId], cancellationToken) is { } r
            ? new StoreState(
                r.StoreNumber,
                r.Name,
                r.Street is null || r.PostalCode is null || r.City is null ? null : new Address(r.Street, r.PostalCode, r.City),
                JsonSerializer.Deserialize<List<OpeningHours>>(r.OpeningHoursJson) ?? [],
                r.IsActive)
            : null;

    public async Task SaveStoreAsync(Guid storeId, StoreState state, CancellationToken cancellationToken)
    {
        var record = await db.StoreStates.FindAsync([storeId], cancellationToken);
        if (record is null)
        {
            record = new StoreStateRecord { StoreId = storeId, StoreNumber = state.StoreNumber, Name = state.Name, OpeningHoursJson = "[]" };
            db.StoreStates.Add(record);
        }

        record.StoreNumber = state.StoreNumber;
        record.Name = state.Name;
        record.Street = state.Address?.Street;
        record.PostalCode = state.Address?.PostalCode;
        record.City = state.Address?.City;
        record.OpeningHoursJson = JsonSerializer.Serialize(state.OpeningHours);
        record.IsActive = state.IsActive;
    }

    public async Task<Guid?> FindStoreIdByNumberAsync(string storeNumber, CancellationToken cancellationToken) =>
        // Stores of the running sync are not saved yet: look at the tracked ones first.
        db.StoreStates.Local.FirstOrDefault(s => s.StoreNumber == storeNumber)?.StoreId
        ?? await db.StoreStates.Where(s => s.StoreNumber == storeNumber).Select(s => (Guid?)s.StoreId).FirstOrDefaultAsync(cancellationToken);

    public async Task<SyncWatermark> GetWatermarkAsync(MasterDataEntity entity, CancellationToken cancellationToken) =>
        await db.SyncWatermarks.FindAsync([entity.ToString()], cancellationToken) is { } r
            ? new SyncWatermark(r.LastModified, r.InitialLoadCompleted)
            : SyncWatermark.None;

    public async Task SaveWatermarkAsync(MasterDataEntity entity, SyncWatermark watermark, CancellationToken cancellationToken)
    {
        var record = await db.SyncWatermarks.FindAsync([entity.ToString()], cancellationToken);
        if (record is null)
        {
            record = new SyncWatermarkRecord { EntityType = entity.ToString() };
            db.SyncWatermarks.Add(record);
        }

        record.LastModified = watermark.LastModified;
        record.InitialLoadCompleted = watermark.InitialLoadCompleted;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
