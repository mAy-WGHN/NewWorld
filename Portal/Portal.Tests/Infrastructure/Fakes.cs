using System.Collections.Concurrent;
using Portal.Authentication;
using Portal.Common.Api;
using Portal.Contracts;
using Portal.Domain.Entities;
using Portal.Services;
using Microsoft.AspNetCore.Http;

namespace Portal.Tests.Infrastructure;

/// <summary>Rollen-Rechte: Welche Rechte hat das Token? Ein Token ohne Eintrag ist dort unbekannt (wie ein deaktivierter User): 401.</summary>
public sealed class FakeAccessDirectory : IAccessDirectory
{
    private readonly ConcurrentDictionary<string, UserAccess> _access = new();

    /// <summary>true = Rollen-Rechte ist nicht erreichbar (503).</summary>
    public bool Unavailable { get; set; }

    public int Calls;

    public void Set(string token, IEnumerable<string> privileges, ScopeSet scope)
    {
        var list = privileges.ToList();
        _access[token] = new UserAccess(list.ToHashSet(StringComparer.Ordinal), list.ToDictionary(p => p, _ => scope, StringComparer.Ordinal));
    }

    /// <summary>Rollen-Rechte nennt das Recht, aber keine Reichweite dazu (sollte nie vorkommen: fail-closed).</summary>
    public void SetPrivilegesOnly(string token, IEnumerable<string> privileges) =>
        _access[token] = new UserAccess(privileges.ToHashSet(StringComparer.Ordinal), new Dictionary<string, ScopeSet>());

    /// <summary>Rollen-Rechte kennt das Token nicht mehr (User deaktiviert).</summary>
    public void Revoke(string token) => _access.TryRemove(token, out _);

    public void Reset()
    {
        Unavailable = false;
        Calls = 0;
    }

    public Task<UserAccess?> ResolveAsync(string token, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (Unavailable) throw new AccessUnavailableException("Rollen-Rechte ist nicht erreichbar.");
        return Task.FromResult(_access.TryGetValue(token, out var access) ? access : null);
    }
}

/// <summary>Die Filialen von Rollen-Rechte.</summary>
public sealed class FakeLocationDirectory : ILocationDirectory
{
    private readonly ConcurrentDictionary<Guid, LocationResponse> _locations = new();

    /// <summary>true = Rollen-Rechte ist nicht erreichbar und es gibt keine zuletzt bekannte Liste (503).</summary>
    public bool Unavailable { get; set; }

    public LocationResponse Add(string name, string storeCode, bool archived = false)
    {
        var location = new LocationResponse(Guid.CreateVersion7(), name, storeCode, archived);
        _locations[location.Id] = location;
        return location;
    }

    public void Reset() => Unavailable = false;

    public Task<IReadOnlyDictionary<Guid, LocationResponse>> GetAllAsync(CancellationToken ct, bool forceRefresh = false) =>
        Unavailable
            ? throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Die Filialen (Rollen-Rechte) sind nicht erreichbar.")
            : Task.FromResult<IReadOnlyDictionary<Guid, LocationResponse>>(_locations.ToDictionary(l => l.Key, l => l.Value));
}

/// <summary>Was das Portal an "Benachrichtigungen" melden würde.</summary>
public sealed class FakeReportEvents : IReportEvents
{
    public sealed record Published(Guid ReportId, IReadOnlyList<Guid> LocationIds);

    private readonly ConcurrentQueue<Published> _published = new();

    public IReadOnlyList<Published> Sent => _published.ToList();

    public void Reset() => _published.Clear();

    public Task ReportCreatedAsync(Report report, CancellationToken ct)
    {
        _published.Enqueue(new Published(report.Id, report.LocationIds.ToList()));
        return Task.CompletedTask;
    }
}
