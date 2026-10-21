namespace Shared.Authorization;

/// <summary>Coarse roles delivered as app roles in the token (Entra ID).</summary>
public static class AppRoles
{
    public const string Employee = "Employee";
    public const string StoreManager = "StoreManager";
    public const string Hr = "Hr";
    public const string Executive = "Executive";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Employee, StoreManager, Hr, Executive,
    };
}
