using System.Reflection;
using System.Text.RegularExpressions;
using Portal.Data;

namespace Portal.Authentication;

/// <summary>Ein vergebbares Recht innerhalb eines Bereichs; <see cref="Privilege"/> ist der gespeicherte Schlüssel (z. B. "report:read").</summary>
public sealed record PrivilegeAction(string Privilege, string Label, bool Scopable = false)
{
    public string Action => Privilege[(Privilege.IndexOf(':') + 1)..];
}

/// <summary>Bereich (Ressource) des Katalogs, z. B. "report" mit seinen Aktionen.</summary>
public sealed record PrivilegeArea(string Key, string Label, IReadOnlyList<PrivilegeAction> Actions, bool DefinesReach = true);

/// <summary>
/// Die Rechte dieser Anwendung. Der Katalog wird <b>generiert</b>: aus jeder Entity mit <see cref="PrivilegeResourceAttribute"/>, die als
/// <c>DbSet</c> im <see cref="IrsDbContext"/> steht (CRUD plus <see cref="PrivilegeActionAttribute"/>-Aktionen), und den wenigen Bereichen ohne
/// Entity in <see cref="CustomAreas"/>. Er wird beim Start bei Rollen-Rechte angemeldet (<see cref="CatalogRegistration"/>), dort in Rollen vergeben und hier
/// an den Routen durchgesetzt. Jedes Recht muss von mindestens einer Route verlangt werden und umgekehrt (<c>PrivilegeCatalogTests</c>).
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

    /// <summary>Bereiche, die keine Entity sind (z. B. reine Auswertungen).</summary>
    private static readonly PrivilegeArea[] CustomAreas =
    [
        new("statistic", "Statistik", [new(Privileges.StatisticRead, "Lesen", Scopable: true)])
    ];

    /// <summary>Entities mit <see cref="PrivilegeResourceAttribute"/>, in der Reihenfolge ihrer <c>DbSet</c>s.</summary>
    public static IReadOnlyList<Type> ResourceEntities { get; } = typeof(IrsDbContext).GetProperties()
        .Select(p => p.PropertyType)
        .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.DbSet<>))
        .Select(t => t.GetGenericArguments()[0])
        .Where(t => t.GetCustomAttribute<PrivilegeResourceAttribute>() is not null)
        .ToList();

    public static IReadOnlyList<PrivilegeArea> Areas { get; } =
        ResourceEntities.Select(AreaFor).Concat(CustomAreas).ToList();

    /// <summary>Alle konkreten Rechte.</summary>
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

    /// <summary>Das zusätzliche Recht <paramref name="action"/> der Entity (<see cref="PrivilegeActionAttribute"/>).</summary>
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
}
