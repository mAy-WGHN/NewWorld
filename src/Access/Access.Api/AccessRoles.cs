namespace Access.Api;

/// <summary>App roles of calling services (service-to-service).</summary>
public static class AccessRoles
{
    /// <summary>An application registers its privilege catalogue.</summary>
    public const string CatalogRegister = "Access.Catalog.Register";

    /// <summary>An API rebuilds its permission/responsibility copy after an outage or a new installation.</summary>
    public const string PermissionsRead = "Access.Permissions.Read";

    public static readonly IReadOnlyList<string> All = [CatalogRegister, PermissionsRead];
}
