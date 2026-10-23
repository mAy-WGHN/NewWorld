namespace BcIntegration.Api;

/// <summary>App roles of callers (service-to-service only; BC Integration has no endpoints for frontends).</summary>
public static class BcIntegrationRoles
{
    /// <summary>Release of an employee's data erasure (for now triggered manually by an operator).</summary>
    public const string EmployeeErasureRequest = "BcIntegration.EmployeeErasure.Request";

    /// <summary>Check of personnel number and PIN for the login (called by Access only).</summary>
    public const string PinVerify = "BcIntegration.Pin.Verify";

    public static readonly IReadOnlyList<string> All = [EmployeeErasureRequest, PinVerify];
}
