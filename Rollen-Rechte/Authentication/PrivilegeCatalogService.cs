using Rollen_Rechte.Data;
using Microsoft.EntityFrameworkCore;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Authentication;

/// <summary>
/// Der vollständige Katalog zu einem Zeitpunkt: die aus Entities generierten Rechte (<see cref="PrivilegeCatalog"/>), die von den Anwendungen registrierten Rechte plus die
/// zur Laufzeit angelegten <see cref="Domain.Entities.CustomPrivilege"/>s, einsortiert in ihren Bereich (Schlüssel vor dem Doppelpunkt).
/// </summary>
public sealed class PrivilegeSnapshot
{
    public IReadOnlyList<PrivilegeArea> Areas { get; }

    /// <summary>Alle konkreten Rechte (generierte und eigene, ohne Platzhalter).</summary>
    public IReadOnlySet<string> All { get; }

    /// <summary>Nur die eigenen Rechte.</summary>
    public IReadOnlySet<string> Custom { get; }

    public PrivilegeSnapshot(IEnumerable<(string Key, string Label)> custom, IEnumerable<AppPrivilege>? apps = null)
    {
        var areas = PrivilegeCatalog.Areas.ToList();

        // Rechte anderer Anwendungen (Portal, HR-Tool, ...): sie registrieren ihren Katalog beim Start. Ein Schlüssel, der schon aus den
        // Entities dieses Dienstes entsteht, bleibt der generierte.
        foreach (var group in (apps ?? []).Where(a => !PrivilegeCatalog.All.Contains(a.Privilege)).GroupBy(a => a.AreaKey).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var actions = group.OrderBy(a => a.Privilege, StringComparer.Ordinal)
                .Select(a => new PrivilegeAction(a.Privilege, a.Label, Custom: false, Scopable: a.Scopable)).ToList();
            var index = areas.FindIndex(a => a.Key == group.Key);
            if (index >= 0) areas[index] = areas[index] with { Actions = [.. areas[index].Actions, .. actions] };
            else areas.Add(new PrivilegeArea(group.Key, group.First().AreaLabel, actions, group.First().DefinesReach));
        }

        var known = areas.SelectMany(a => a.Actions).Select(a => a.Privilege).ToHashSet(StringComparer.Ordinal);
        var own = custom.Where(c => !known.Contains(c.Key)).OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

        foreach (var group in own.GroupBy(c => c.Key[..c.Key.IndexOf(':')]))
        {
            var actions = group.Select(c => new PrivilegeAction(c.Key, c.Label, Custom: true)).ToList();
            var index = areas.FindIndex(a => a.Key == group.Key);
            if (index >= 0) areas[index] = areas[index] with { Actions = [.. areas[index].Actions, .. actions] };
            else areas.Add(new PrivilegeArea(group.Key, group.Key, actions)); // eigener Bereich: Label = Schlüssel
        }

        Areas = areas;
        Custom = own.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        All = areas.SelectMany(a => a.Actions).Select(a => a.Privilege).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Kann dieses Recht auf einen Scope beschränkt vergeben werden? Nur Rechte, deren Aktion als scopbar freigegeben ist (generiert oder von der Anwendung registriert).</summary>
    public bool IsScopable(string privilege) =>
        Areas.SelectMany(a => a.Actions).Any(a => a.Privilege == privilege && a.Scopable);

    /// <summary>
    /// Bestimmt eine Zuweisung mit diesen Rechten die Datenreichweite? Ja, wenn sie ein scopbares Recht eines Bereichs mit <see cref="PrivilegeArea.DefinesReach"/> enthält
    /// (Meldungen, Filialen, User, Statistik, ...) — nicht, wenn sie nur Rollen oder Scopes verwaltet.
    /// </summary>
    public bool DefinesReach(IEnumerable<string> grantPrivileges)
    {
        var list = grantPrivileges.ToList();
        return Areas.Where(a => a.DefinesReach).SelectMany(a => a.Actions).Any(a => a.Scopable && Privileges.Allows(list, a.Privilege));
    }
    /// <summary>Erlaubt in einer Rolle: ein bekanntes Recht, <c>*</c> oder <c>bereich:*</c> eines bekannten Bereichs.</summary>
    public bool IsKnown(string privilege) =>
        privilege == Privileges.Everything
        || All.Contains(privilege)
        || (privilege.EndsWith(":*", StringComparison.Ordinal) && Areas.Any(a => a.Key + ":*" == privilege));

    /// <summary>Die konkreten Rechte, die <paramref name="granted"/> (auch mit Platzhaltern) abdeckt; sortiert.</summary>
    public IReadOnlyList<string> Expand(IEnumerable<string> granted)
    {
        var list = granted.ToList();
        return All.Where(p => Privileges.Allows(list, p)).Order(StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// Liefert den <see cref="PrivilegeSnapshot"/> und hält ihn kurz im Speicher. Änderungen an eigenen Rechten rufen
/// <see cref="Invalidate"/> auf; weitere Instanzen sehen sie spätestens nach <see cref="Ttl"/>.
/// </summary>
public sealed class PrivilegeCatalogService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PrivilegeCatalogService> logger)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private PrivilegeSnapshot? _snapshot;
    private DateTimeOffset _loadedAt;

    public void Invalidate() => _snapshot = null;

    public async Task<PrivilegeSnapshot> GetAsync(CancellationToken ct = default)
    {
        if (_snapshot is { } fresh && clock.GetUtcNow() - _loadedAt < Ttl) return fresh;

        await _lock.WaitAsync(ct);
        try
        {
            if (_snapshot is { } again && clock.GetUtcNow() - _loadedAt < Ttl) return again;

            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
                var rows = await db.CustomPrivileges.AsNoTracking().Select(c => new { c.Key, c.Label }).ToListAsync(ct);
                var apps = await db.AppPrivileges.AsNoTracking().ToListAsync(ct);
                _snapshot = new PrivilegeSnapshot(rows.Select(r => (r.Key, r.Label)), apps);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tabelle fehlt (Migration noch nicht angewendet) o. Ä.: mit den generierten Rechten weiterarbeiten.
                logger.LogWarning(ex, "Eigene Rechte konnten nicht geladen werden; es gelten nur die generierten Rechte.");
                _snapshot = new PrivilegeSnapshot([]);
            }

            _loadedAt = clock.GetUtcNow();
            return _snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }
}
