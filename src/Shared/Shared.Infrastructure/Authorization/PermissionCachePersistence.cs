using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Authorization;

namespace Shared.Infrastructure.Authorization;

public static class PermissionCachePersistence
{
    /// <summary>Persists the permission cache in the service's own database and restores it at start (last known state).</summary>
    public static IServiceCollection AddPermissionCachePersistence<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddScoped<IPermissionStore, EfPermissionStore<TContext>>();
        services.AddScoped<PermissionsChangedHandler>();
        services.AddHostedService<PermissionCacheLoader>();
        return services;
    }
}

internal sealed partial class PermissionCacheLoader(
    IServiceScopeFactory scopes,
    IPermissionCache cache,
    ILogger<PermissionCacheLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var snapshots = await scope.ServiceProvider.GetRequiredService<IPermissionStore>().LoadAllAsync(cancellationToken);
            foreach (var snapshot in snapshots)
            {
                cache.Apply(snapshot);
            }
        }
#pragma warning disable CA1031 // start without cache (fail closed) rather than not at all; readiness reports the database
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogLoadFailed(ex.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Permission cache could not be restored: {ExceptionType}. Starting empty (fail closed).")]
    private partial void LogLoadFailed(string exceptionType);
}
