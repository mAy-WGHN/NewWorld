using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Authorization;

namespace Shared.Infrastructure.Authorization;

/// <summary>A privilege an application brings: <c>resource:action</c>; scopable = can be restricted to stores.</summary>
public sealed record CatalogPrivilege(string Key, bool Scopable);

public enum RegistrationResult
{
    Unknown = 0,
    Registered = 1,

    /// <summary>Access not reachable or not ready: try again later.</summary>
    Retry = 2,

    /// <summary>Access refused the catalogue (key owned by another application, malformed): retrying cannot help.</summary>
    Rejected = 3,
}

/// <summary>
/// Registers the application's privileges with Access (service-to-service, app role <c>Access.Catalog.Register</c>) at start and keeps
/// trying until Access answers, so every service starts without Access (last known state). Only privileges that are registered
/// take effect in the ACL. Logs status and key counts, never content.
/// </summary>
public sealed partial class CatalogRegistrationService(
    HttpClient http,
    string appKey,
    IReadOnlyList<CatalogPrivilege> privileges,
    TimeProvider time,
    TimeSpan retryInterval,
    ILogger<CatalogRegistrationService> logger) : BackgroundService
{
    public async Task<RegistrationResult> RegisterOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PutAsJsonAsync(
                $"/internal/apps/{appKey}/catalog",
                new { privileges = privileges.Select(p => new { key = p.Key, scopable = p.Scopable }) },
                cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent or HttpStatusCode.OK => RegistrationResult.Registered,
                HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => RegistrationResult.Rejected,
                _ => RegistrationResult.Retry,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return RegistrationResult.Retry;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(retryInterval, time);
        do
        {
            switch (await RegisterOnceAsync(stoppingToken))
            {
                case RegistrationResult.Registered:
                    LogRegistered(appKey, privileges.Count);
                    return;
                case RegistrationResult.Rejected:
                    LogRejected(appKey);
                    return;
                default:
                    LogRetry(appKey);
                    break;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue of {AppKey} registered with Access ({Count} privileges)")]
    private partial void LogRegistered(string appKey, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Access refused the catalogue of {AppKey}; not retrying")]
    private partial void LogRejected(string appKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalogue of {AppKey} not registered yet, will retry")]
    private partial void LogRetry(string appKey);
}

public static class CatalogRegistrationExtensions
{
    /// <summary>
    /// Registers <paramref name="privileges"/> of <paramref name="appKey"/> with Access in the background. Configuration:
    /// <c>Access:BaseUrl</c>, <c>Access:Scope</c>, <c>Access:Credential</c> (credential kind).
    /// </summary>
    public static IServiceCollection AddCatalogRegistration(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, string appKey, IReadOnlyList<CatalogPrivilege> privileges)
    {
        var credentialKind = configuration["Access:Credential"];
        if (credentialKind == TokenCredentialFactory.LocalFake && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("The local fake credential is only allowed in Development/Testing.");
        }

        services.AddKeyedSingleton<TokenCredential>("access", (_, _) => TokenCredentialFactory.Create(credentialKind));
        services.AddHttpClient("access-catalog", http => http.BaseAddress = new Uri(configuration["Access:BaseUrl"] ?? "http://access-api"))
            .AddHttpMessageHandler(sp => new TokenCredentialHandler(
                sp.GetRequiredKeyedService<TokenCredential>("access"),
                configuration["Access:Scope"] ?? "api://access/.default",
                sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService(sp => new CatalogRegistrationService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("access-catalog"), appKey, privileges, sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(30), sp.GetRequiredService<ILogger<CatalogRegistrationService>>()));
        return services;
    }
}
