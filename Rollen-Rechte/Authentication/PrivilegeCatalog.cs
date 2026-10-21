using System.Reflection;
using System.Text.RegularExpressions;
using Rollen_Rechte.Data;

namespace Rollen_Rechte.Authentication;

/// <summary>Ein vergebbares Recht innerhalb eines Bereichs; <see cref="Privilege"/> ist der gespeicherte Schlüssel (z. B. "report:read").</summary>
public sealed record PrivilegeAction(string Privilege, string Label, bool Custom = false, bool Scopable = false)
{
    public string Action => Privilege[(Privilege.IndexOf(':') + 1)..];
}

/// <summary>Bereich (Ressource) des Katalogs, z. B. "report" mit seinen Aktionen.</summary>
public sealed record PrivilegeArea(string Key, string Label, IReadOnlyList<PrivilegeAction> Actions, bool DefinesReach = true);

/// <summary>
/// Einzige Quelle dafür, welche Rechte es gibt: Die UI baut daraus die Rollen-Matrix (<c>GET /api/privileges</c>), das Backend prüft
/// beim Speichern einer Rolle dagegen. Der Katalog wird <b>generiert</b>: aus jeder Entity mit <see cref="PrivilegeResourceAttribute"/>, die als
/// <c>DbSet</c> im <see cref="AccessDbContext"/> steht (CRUD plus <see cref="PrivilegeActionAttribute"/>-Aktionen), und den wenigen Bereichen ohne
/// Entity in <see cref="CustomAreas"/>. Neue Entity: Attribut setzen, Routen mit <c>[HasPrivilege&lt;Entity&gt;(…)]</c> versehen — fertig.
/// Jedes Recht muss von mindestens einer Route verlangt werden und umgekehrt (<c>PrivilegeTests</c>).
/// </summary>
public static class PrivilegeCatalog
{
    private static readonly (CrudAction Flag, string Key, string Label)[] CrudActions =
    [
        (CrudAction.Read, "read", "Lesen"),
        (CrudAction.Create, "create", "Erstellen"),
        (CrudAction.Update, "update", "Ändern"),
        (CrudAction.Delete, "delete", "Löschen")
    ];

    /// <summary>Bereiche dieses Dienstes, die keine Entity sind. Derzeit keine: Die Rechte der Anwendungen (Meldungen, FAQ, Statistik, ...) registrieren diese selbst.</summary>
    private static readonly PrivilegeArea[] CustomAreas = [];

    /// <summary>Entities mit <see cref="PrivilegeResourceAttribute"/>, in der Reihenfolge ihrer <c>DbSet</c>s.</summary>
    public static IReadOnlyList<Type> ResourceEntities { get; } = typeof(AccessDbContext).GetProperties()
        .Select(p => p.PropertyType)
        .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.DbSet<>))
        .Select(t => t.GetGenericArguments()[0])
        .Where(t => t.GetCustomAttribute<PrivilegeResourceAttribute>() is not null)
        .ToList();

    public static IReadOnlyList<PrivilegeArea> Areas { get; } =
        ResourceEntities.Select(AreaFor).Concat(CustomAreas).ToList();

    /// <summary>Alle konkreten Rechte (ohne Platzhalter).</summary>
    public static IReadOnlySet<string> All { get; } =
        Areas.SelectMany(a => a.Actions).Select(a => a.Privilege).ToHashSet(StringComparer.Ordinal);

    /// <summary>Schlüssel des Bereichs einer Entity (<see cref="PrivilegeResourceAttribute.Key"/> oder der Klassenname in snake_case).</summary>
    public static string KeyOf(Type entity)
    {
        var attribute = ResourceOf(entity);
        return attribute.Key ?? Regex.Replace(entity.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
    }

    /// <summary>Der generierte Bereich einer Entity: Standardaktionen laut <see cref="PrivilegeResourceAttribute.Actions"/> plus ihre <see cref="PrivilegeActionAttribute"/>s.</summary>
    public static PrivilegeArea AreaFor(Type entity)
    {
        var attribute = ResourceOf(entity);
        var key = KeyOf(entity);
        var actions = CrudActions
            .Where(c => attribute.Actions.HasFlag(c.Flag))
            .Select(c => new PrivilegeAction($"{key}:{c.Key}", c.Flag == CrudAction.Delete ? attribute.DeleteLabel ?? c.Label : c.Label, Scopable: attribute.ScopedActions.HasFlag(c.Flag)))
            .Concat(entity.GetCustomAttributes<PrivilegeActionAttribute>().Select(a => new PrivilegeAction($"{key}:{a.Action}", a.Label, Scopable: a.Scopable)))
            .ToList();
        return new PrivilegeArea(key, attribute.Label, actions, attribute.DefinesReach);
    }

    /// <summary>Das Recht <paramref name="action"/> der Entity, z. B. <c>report:read</c>; die Aktion muss an der Entity freigegeben sein.</summary>
    public static string PrivilegeOf(Type entity, CrudAction action)
    {
        var crud = CrudActions.SingleOrDefault(c => c.Flag == action);
        if (crud.Key is null) throw new ArgumentException($"Genau eine Aktion erwartet, nicht {action}.", nameof(action));
        return RequireDeclared(entity, crud.Key);
    }

    /// <summary>Das zusätzliche Recht <paramref name="action"/> der Entity (<see cref="PrivilegeActionAttribute"/>), z. B. <c>role:assign</c>.</summary>
    public static string PrivilegeOf(Type entity, string action) => RequireDeclared(entity, action);

    private static string RequireDeclared(Type entity, string action)
    {
        var privilege = $"{KeyOf(entity)}:{action}";
        if (!AreaFor(entity).Actions.Any(a => a.Privilege == privilege))
            throw new InvalidOperationException($"{entity.Name} hat kein Recht \"{privilege}\" (PrivilegeResource.Actions bzw. PrivilegeAction prüfen).");
        return privilege;
    }

    private static PrivilegeResourceAttribute ResourceOf(Type entity) =>
        entity.GetCustomAttribute<PrivilegeResourceAttribute>()
        ?? throw new InvalidOperationException($"{entity.Name} trägt kein [PrivilegeResource].");

    /// <summary>Erlaubt in einer Rolle: ein Recht des Katalogs, <c>*</c> oder <c>bereich:*</c> eines bekannten Bereichs.</summary>
    public static bool IsKnown(string privilege) =>
        privilege == Privileges.Everything
        || All.Contains(privilege)
        || (privilege.EndsWith(":*", StringComparison.Ordinal) && Areas.Any(a => a.Key + ":*" == privilege));

    /// <summary>Die konkreten Rechte des Katalogs, die <paramref name="granted"/> (auch mit Platzhaltern) abdeckt; sortiert.</summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> granted)
    {
        var list = granted.ToList();
        return All.Where(p => Privileges.Allows(list, p)).Order(StringComparer.Ordinal).ToList();
    }
}
