using Access.Core.Acl;
using Access.Core.Login;
using Access.Core.People;
using Microsoft.EntityFrameworkCore;

namespace Access.Infrastructure.Login;

public sealed class EfPersonDirectory(AccessDbContext db) : IPersonDirectory
{
    public async Task<Person?> FindByPersonnelNumberAsync(string personnelNumber, CancellationToken cancellationToken) =>
        ToPerson(await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.PersonnelNumber == personnelNumber, cancellationToken));

    public async Task<Person?> FindAsync(Guid employeeId, CancellationToken cancellationToken) =>
        ToPerson(await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken));

    public async Task<bool> FormerEmployeesMayLoginAsync(CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().AnyAsync(r => r.Id == Role.FormerEmployeeId && r.Privileges.Count > 0, cancellationToken);

    private static Person? ToPerson(People.PersonRecord? p) =>
        p is null ? null : new Person(p.EmployeeId, p.PersonnelNumber, p.Name, p.HomeStoreId, p.Position, p.ExitDate);
}

public sealed class EfCredentialStore(AccessDbContext db) : ICredentialStore
{
    public async Task<StoredCredential?> GetAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.Credentials.AsNoTracking().FirstOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken) is { } r
            ? new StoredCredential(r.PinHash, r.EncryptedPin, r.VerifiedAt)
            : null;

    public async Task SaveAsync(Guid employeeId, StoredCredential credential, CancellationToken cancellationToken)
    {
        var record = await db.Credentials.FindAsync([employeeId], cancellationToken);
        if (record is null)
        {
            record = new CredentialRecord { EmployeeId = employeeId, PinHash = credential.PinHash };
            db.Credentials.Add(record);
        }

        record.PinHash = credential.PinHash;
        record.EncryptedPin = credential.EncryptedPin;
        record.VerifiedAt = credential.VerifiedAt;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class EfRefreshTokenStore(AccessDbContext db) : IRefreshTokenStore
{
    public async Task AddAsync(RefreshTokenEntry entry, CancellationToken cancellationToken)
    {
        db.RefreshTokens.Add(new RefreshTokenRecord { Id = entry.Id, EmployeeId = entry.EmployeeId, TokenHash = entry.TokenHash, ExpiresAt = entry.ExpiresAt, Used = entry.Used });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshTokenEntry?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken) is { } r
            ? new RefreshTokenEntry(r.Id, r.EmployeeId, r.TokenHash, r.ExpiresAt, r.Used)
            : null;

    /// <summary>Atomic: of two parallel refreshes with the same token only one wins (the other sees <c>Used</c> and revokes everything).</summary>
    public async Task MarkUsedAsync(Guid id, CancellationToken cancellationToken) =>
        await db.RefreshTokens.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Used, true), cancellationToken);

    public async Task RevokeAllAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.RefreshTokens.Where(t => t.EmployeeId == employeeId).ExecuteDeleteAsync(cancellationToken);
}
