using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Persistence;

/// <summary>
/// A lock across all instances of a service for work that must run on one instance at a time (periodic background work such as the master data
/// sync or the maintenance of certificates). The lock is not waited for: whoever does not get it skips the round and tries again at the next
/// interval. Whoever holds it and dies loses it with the database connection, so a crashed instance never blocks the others.
/// </summary>
public interface IDistributedLock
{
    /// <summary>The lock of this name, or <c>null</c> if another instance holds it. Dispose the result to release the lock.</summary>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IDistributedLock"/> on a PostgreSQL session advisory lock (<c>pg_try_advisory_lock</c>) of the service's own database: no new
/// package, no table, no migration. The lock lives on its own open connection for as long as the handle exists; the work itself uses other
/// connections of its own scope.
/// </summary>
public sealed class PostgresAdvisoryLock<TContext>(IServiceScopeFactory scopes) : IDistributedLock where TContext : DbContext
{
    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        try
        {
            var database = scope.ServiceProvider.GetRequiredService<TContext>().Database;
            await database.OpenConnectionAsync(cancellationToken);
            var key = KeyOf(name);
            var acquired = (await database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({key}) AS \"Value\"").ToListAsync(cancellationToken)).Single();
            if (acquired)
            {
                return new Handle(scope, database, key);
            }

            await database.CloseConnectionAsync();
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }

        await scope.DisposeAsync();
        return null;
    }

    /// <summary>A stable 64 bit key from the name (the first eight bytes of its SHA-256), so every instance computes the same key.</summary>
    internal static long KeyOf(string name) => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("newworld:" + name)), 0);

    private sealed class Handle(AsyncServiceScope scope, Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await database.SqlQuery<bool>($"SELECT pg_advisory_unlock({key}) AS \"Value\"").ToListAsync(CancellationToken.None);
            }
            finally
            {
                // closing the connection would also release the lock; the explicit unlock keeps a pooled connection clean
                await scope.DisposeAsync();
            }
        }
    }
}

public static class DistributedLockServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IDistributedLock"/> on the advisory locks of the database of <typeparamref name="TContext"/>.</summary>
    public static IServiceCollection AddPostgresAdvisoryLock<TContext>(this IServiceCollection services) where TContext : DbContext =>
        services.AddSingleton<IDistributedLock, PostgresAdvisoryLock<TContext>>();
}
