namespace Access.Core.Acl;

/// <summary>A registered privilege. Only registered privileges take effect.</summary>
/// <param name="Key">Privilege <c>resource:action</c>.</param>
/// <param name="Owner">Application that registered it (e.g. <c>workforce</c>), <c>access</c>, or <c>custom</c> for own privileges.</param>
/// <param name="Scopable">Can be restricted to stores; otherwise it takes effect only in unrestricted assignments.</param>
/// <param name="Description">What the privilege allows, in plain words (German, shown when roles are put together).</param>
public sealed record CatalogEntry(string Key, string Owner, bool Scopable, string Description = "");

/// <summary>The privileges of the Access service itself (administration of the ACL).</summary>
public static class AccessPrivileges
{
    public const string Owner = "access";

    public const string RoleRead = "access:role:read";
    public const string RoleCreate = "access:role:create";
    public const string RoleUpdate = "access:role:update";
    public const string RoleDelete = "access:role:delete";
    public const string ScopeRead = "access:scope:read";
    public const string ScopeCreate = "access:scope:create";
    public const string ScopeUpdate = "access:scope:update";
    public const string ScopeDelete = "access:scope:delete";
    public const string AssignmentRead = "access:assignment:read";
    public const string AssignmentCreate = "access:assignment:create";
    public const string AssignmentDelete = "access:assignment:delete";
    public const string ResponsibilityRead = "access:responsibility:read";
    public const string ResponsibilityCreate = "access:responsibility:create";
    public const string ResponsibilityDelete = "access:responsibility:delete";
    public const string CatalogRead = "access:catalog:read";
    public const string CustomPrivilegeCreate = "access:custom-privilege:create";
    public const string CustomPrivilegeDelete = "access:custom-privilege:delete";
    public const string PersonRead = "access:person:read";
    public const string MainStoreUpdate = "access:main-store:update";

    public static readonly IReadOnlyList<CatalogEntry> Catalog =
    [
        new(RoleRead, Owner, false, "Rollen und ihre Rechte ansehen."),
        new(RoleCreate, Owner, false, "Neue Rollen anlegen (nur mit Rechten, die man selbst hat)."),
        new(RoleUpdate, Owner, false, "Die Rechte einer Rolle ändern (nur im Rahmen der eigenen Rechte)."),
        new(RoleDelete, Owner, false, "Rollen löschen, die niemandem mehr zugewiesen sind."),
        new(ScopeRead, Owner, false, "Filialgruppen (Scopes: Region, Bezirk) ansehen."),
        new(ScopeCreate, Owner, false, "Neue Filialgruppen anlegen."),
        new(ScopeUpdate, Owner, true, "Die Filialen einer Filialgruppe ändern (nur Filialen in der eigenen Reichweite)."),
        new(ScopeDelete, Owner, false, "Filialgruppen löschen, die nirgends mehr verwendet werden."),
        new(AssignmentRead, Owner, true, "Sehen, welche Rollen Personen in der eigenen Reichweite zugewiesen sind."),
        new(AssignmentCreate, Owner, true, "Personen in der eigenen Reichweite eine Rolle zuweisen, mit Reichweite und Laufzeit (nie mehr, als man selbst hat)."),
        new(AssignmentDelete, Owner, true, "Rollenzuweisungen von Personen in der eigenen Reichweite entziehen."),
        new(ResponsibilityRead, Owner, true, "Sehen, wer für eine Person zuständig ist (z. B. Genehmiger für Abwesenheiten)."),
        new(ResponsibilityCreate, Owner, true, "Zuständigkeiten für ein Thema einrichten (z. B. Filialleitung genehmigt Urlaub), mit Laufzeit."),
        new(ResponsibilityDelete, Owner, true, "Zuständigkeiten entfernen."),
        new(CatalogRead, Owner, false, "Den Katalog aller Rechte mit Beschreibung ansehen."),
        new(CustomPrivilegeCreate, Owner, false, "Eigene Rechte anlegen, die in keinem Dienst fest eingebaut sind."),
        new(CustomPrivilegeDelete, Owner, false, "Eigene Rechte löschen."),
        new(PersonRead, Owner, true, "Die Personenkopie in Access (Name, Hauptfiliale, BC-Vorschlag) für Personen in der Reichweite ansehen."),
        new(MainStoreUpdate, Owner, true, "Die Hauptfiliale einer Person festlegen oder auf den BC-Vorschlag zurücksetzen; nur für Personen und Filialen in der Reichweite."),
    ];
}
