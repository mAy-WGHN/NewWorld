using Rollen_Rechte.Authentication;

namespace Rollen_Rechte.Domain.Entities;

/// <summary>Bekannte Dimensionen, in denen ein <see cref="Scope"/> Mitglieder haben kann (<see cref="ScopeMember.Dimension"/>).</summary>
public static class ScopeDimensions
{
    /// <summary>Filialen (<see cref="Location"/>): Meldungen, Filialen und Benutzer dieser Filialen.</summary>
    public const string Location = "location";

    /// <summary>Einzelne Meldungen (<see cref="Report"/>), z. B. für eine Zweitmeinung.</summary>
    public const string Report = "report";

    public static readonly IReadOnlyList<string> All = [Location, Report];
}

/// <summary>
/// Benannte Reichweite (z. B. "Region Nord"), pflegbar in der UI und an <see cref="AclUserRole"/>-Zuweisungen gehängt:
/// Die Rolle sagt, <b>was</b> jemand darf, der Scope, <b>wo</b>. Mitglieder sind allgemein (<see cref="ScopeMember"/>),
/// eine neue Dimension braucht daher keine Schemaänderung.
/// </summary>
[PrivilegeResource("Scopes", ScopedActions = CrudAction.Read, DefinesReach = false)]
public class Scope
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<ScopeMember> Members { get; set; } = [];

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Mitglied eines Scopes: <see cref="Dimension"/> (z. B. "location") und die ID des Objekts darin. Kein Fremdschlüssel auf das Objekt (dimensionsübergreifend).</summary>
public class ScopeMember
{
    public Guid ScopeId { get; set; }
    public Scope Scope { get; set; } = null!;

    public string Dimension { get; set; } = string.Empty;

    public Guid RefId { get; set; }
}

/// <summary>Scope einer Rollenzuweisung (<see cref="AclUserRole.Scopes"/>).</summary>
public class AclUserRoleScope
{
    public Guid UserId { get; set; }
    public Guid AclRoleId { get; set; }

    public Guid ScopeId { get; set; }
    public Scope Scope { get; set; } = null!;
}
