using System.Security.Cryptography;
using System.Text;
using Access.Core.People;

namespace Access.Core.Login;

public enum RefreshStatus
{
    Unknown = 0,
    Refreshed = 1,
    Rejected = 2,
}

public sealed record RefreshResult(RefreshStatus Status, Guid? EmployeeId = null, string? NewToken = null)
{
    public static readonly RefreshResult Rejected = new(RefreshStatus.Rejected);
}

/// <summary>
/// Refresh tokens: random, stored only as SHA-256 hash, single use with rotation. Presenting a used token again (theft or replay)
/// revokes all sessions of that person. A person who has left (end of the exit date) cannot refresh any more.
/// </summary>
public sealed class SessionService(IRefreshTokenStore store, IPersonDirectory directory, TimeProvider time, LoginOptions options, TimeZoneInfo zone)
{
    public async Task<string> StartAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await store.AddAsync(
            new RefreshTokenEntry(Guid.CreateVersion7(time.GetUtcNow()), employeeId, Hash(token), time.GetUtcNow() + options.RefreshTokenLifetime, Used: false),
            cancellationToken);
        return token;
    }

    public async Task<RefreshResult> RefreshAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || await store.FindByHashAsync(Hash(token), cancellationToken) is not { } entry)
        {
            return RefreshResult.Rejected;
        }

        if (entry.Used)
        {
            await store.RevokeAllAsync(entry.EmployeeId, cancellationToken);
            return RefreshResult.Rejected;
        }

        if (entry.ExpiresAt <= time.GetUtcNow())
        {
            return RefreshResult.Rejected;
        }

        if (await directory.FindAsync(entry.EmployeeId, cancellationToken) is not { } person || !person.IsActiveAt(time.GetUtcNow(), zone))
        {
            await store.RevokeAllAsync(entry.EmployeeId, cancellationToken);
            return RefreshResult.Rejected;
        }

        await store.MarkUsedAsync(entry.Id, cancellationToken);
        return new RefreshResult(RefreshStatus.Refreshed, entry.EmployeeId, await StartAsync(entry.EmployeeId, cancellationToken));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
