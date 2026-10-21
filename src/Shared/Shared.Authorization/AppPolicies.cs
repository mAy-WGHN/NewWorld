namespace Shared.Authorization;

public static class AppPolicies
{
    public const string RequireEmployee = "App.Employee";
    public const string RequireStoreManager = "App.StoreManager";
    public const string RequireHr = "App.Hr";
    public const string RequireExecutive = "App.Executive";

    /// <summary>Resource policy for <see cref="StoreResource"/>.</summary>
    public const string CanAccessStore = "App.CanAccessStore";

    /// <summary>Resource policy for <see cref="EmployeeResource"/>.</summary>
    public const string CanAccessEmployee = "App.CanAccessEmployee";
}
