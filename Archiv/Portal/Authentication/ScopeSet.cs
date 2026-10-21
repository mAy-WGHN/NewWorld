namespace Portal.Authentication;

/// <summary>
/// Reichweite, in der ein Recht für einen User gilt (von Rollen-Rechte aufgelöst): <see cref="All"/> (uneingeschränkt) oder die Filialen/Meldungen aus seinen
/// Scopes. Eine leere, nicht uneingeschränkte Menge heißt: Das Recht besteht, reicht aber nirgends hin (fail-closed).
/// </summary>
public sealed record ScopeSet(bool All, IReadOnlySet<Guid> Locations, IReadOnlySet<Guid> Reports)
{
    private static readonly IReadOnlySet<Guid> NoIds = new HashSet<Guid>();

    public static ScopeSet Everything { get; } = new(true, NoIds, NoIds);

    public static ScopeSet Of(IEnumerable<Guid> locations, IEnumerable<Guid> reports) =>
        new(false, locations.ToHashSet(), reports.ToHashSet());

    public bool AllowsLocation(Guid locationId) => All || Locations.Contains(locationId);
}

/// <summary>Der Scope, den die Rechteprüfung für die laufende Anfrage ermittelt hat; die Abfragen (z. B. Meldungen) wenden ihn an. Ohne Anfrage-Kontext: uneingeschränkt.</summary>
public sealed class ScopeContext
{
    public ScopeSet Current { get; set; } = ScopeSet.Everything;
}
