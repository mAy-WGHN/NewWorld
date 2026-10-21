using Access.Contracts;

namespace Access.Contracts.Tests;

/// <summary>One sample per message.</summary>
internal static class Samples
{
    public static readonly PermissionsChanged PermissionsChanged = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1)), // after the change back to winter time
        EmployeeId: Guid.Parse("0197a000-0000-7000-8000-000000000001"),
        Version: 7,
        ExternalIdentities: [new ExternalIdentity("entra", "6f1c2b9e-0000-4000-8000-00000000abcd")],
        StoreIds: [Guid.Parse("0197a000-0000-7000-8000-000000000101"), Guid.Parse("0197a000-0000-7000-8000-000000000102")],
        EmployeeIds: [Guid.Parse("0197a000-0000-7000-8000-000000000001")],
        RoleAdditions: []);
}
