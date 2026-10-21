namespace Portal.Authentication;

/// <summary>Die vier Standardaktionen einer Ressource; als Flags kombinierbar (<see cref="PrivilegeResourceAttribute.Actions"/>).</summary>
[Flags]
public enum CrudAction
{
    Read = 1,
    Create = 2,
    Update = 4,
    Delete = 8,
    All = Read | Create | Update | Delete
}

/// <summary>
/// Macht eine Entity zur Rechte-Ressource: Für jede Entity mit diesem Attribut, die als <c>DbSet</c> im <c>IrsDbContext</c> steht,
/// erzeugt der <see cref="PrivilegeCatalog"/> automatisch <c>&lt;key&gt;:read</c>, <c>:create</c>, <c>:update</c> und <c>:delete</c>
/// (Label, Matrix-Eintrag, Validierung in Rollen). Die Routen verlangen sie mit <c>[HasPrivilege&lt;Entity&gt;(CrudAction.Read)]</c>.
/// Nur Entities mit diesem Attribut haben Rechte; Verbindungs- und Hilfstabellen bleiben ohne.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PrivilegeResourceAttribute(string label) : Attribute
{
    /// <summary>Deutscher Name des Bereichs in der Rollen-Matrix, z. B. "Meldungen".</summary>
    public string Label { get; } = label;

    /// <summary>Schlüssel vor dem Doppelpunkt; Standard: Klassenname in snake_case (<c>Report</c> → <c>report</c>, <c>FaqImage</c> → <c>faq_image</c>).</summary>
    public string? Key { get; init; }

    /// <summary>
    /// Welche Standardaktionen es gibt. Standard: alle vier. Einschränken nur, wenn die Aktion fachlich nicht existiert
    /// (z. B. das Anlegen einer Meldung ist öffentlich) — sonst gäbe es ein Recht ohne Wirkung (<c>PrivilegeTests</c> verlangt eine Route je Recht).
    /// </summary>
    public CrudAction Actions { get; init; } = CrudAction.All;

    /// <summary>Anderes Label für "delete", wenn nur archiviert/deaktiviert wird (z. B. "Archivieren").</summary>
    public string? DeleteLabel { get; init; }

    /// <summary>
    /// Für welche Standardaktionen Rollenzuweisungen auf einen Scope (Filialen/Meldungen) beschränkt sein können. Standard: keine — dort zählen nur
    /// uneingeschränkte Zuweisungen (sicherer Standard, z. B. für Rollen, FAQ). Eine Aktion hier freizugeben verlangt, dass die Abfragen
    /// der Ressource den Scope anwenden (<see cref="ScopeContext"/>).
    /// </summary>
    public CrudAction ScopedActions { get; init; }

    /// <summary>
    /// Ob der Scope bei dieser Ressource die <b>Datenreichweite</b> eines Users bestimmt (Meldungen, Filialen, User, Statistik). Bei Verwaltungsobjekten
    /// (Rollen, Scopes) gilt false: Dort steuert der Scope nur, welche Einträge man sieht, und zählt nicht für die Frage, wie weit jemand reicht.
    /// </summary>
    public bool DefinesReach { get; init; } = true;
}

/// <summary>Zusätzliche Aktion einer Ressource jenseits von CRUD, z. B. <c>role:assign</c>; eine Route verlangt sie mit <c>[HasPrivilege&lt;Entity&gt;("assign")]</c>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PrivilegeActionAttribute(string action, string label) : Attribute
{
    /// <summary>Darf diese Aktion auf einen Scope beschränkt vergeben werden (z. B. Rollen nur innerhalb der eigenen Region zuweisen)?</summary>
    public bool Scopable { get; init; }

    public string Action { get; } = action;
    public string Label { get; } = label;
}
