namespace Rollen_Rechte.Authentication;

/// <summary>
/// Reichweite, in der ein Recht für einen User gilt: <see cref="All"/> (uneingeschränkt) oder die Filialen/Meldungen aus seinen
/// Scopes. Eine leere, nicht uneingeschränkte Menge heißt: Das Recht besteht, reicht aber nirgends hin (fail-closed).
/// </summary>
public sealed record ScopeSet(bool All, IReadOnlySet<Guid> Locations, IReadOnlySet<Guid> Reports)
{
    private static readonly IReadOnlySet<Guid> NoIds = new HashSet<Guid>();

    public static ScopeSet Everything { get; } = new(true, NoIds, NoIds);

    public static ScopeSet Of(IEnumerable<Guid> locations, IEnumerable<Guid> reports) =>
        new(false, locations.ToHashSet(), reports.ToHashSet());

    /// <summary>Vereinigung: uneingeschränkt, sobald eine Seite es ist.</summary>
    public ScopeSet Union(ScopeSet other) =>
        All || other.All ? Everything : new(false, Locations.Union(other.Locations).ToHashSet(), Reports.Union(other.Reports).ToHashSet());

    /// <summary>Deckt diese Reichweite die <paramref name="other"/> vollständig ab? (Wer delegiert, darf nicht mehr Reichweite vergeben, als er hat.)</summary>
    public bool Covers(ScopeSet other) =>
        All || (!other.All && other.Locations.IsSubsetOf(Locations) && other.Reports.IsSubsetOf(Reports));

    public bool AllowsLocation(Guid locationId) => All || Locations.Contains(locationId);

    /// <summary>Eine der Filialen genügt (z. B. Meldung mit mehreren Filialen, Rundmail an eine Region).</summary>
    public bool AllowsAnyLocation(IEnumerable<Guid> locationIds) => All || locationIds.Any(Locations.Contains);
}

/// <summary>Der Scope, den die Rechteprüfung für die laufende Anfrage ermittelt hat; die Abfragen (z. B. Meldungen) wenden ihn an. Ohne Anfrage-Kontext: uneingeschränkt.</summary>
public sealed class ScopeContext
{
    public ScopeSet Current { get; set; } = ScopeSet.Everything;
}
