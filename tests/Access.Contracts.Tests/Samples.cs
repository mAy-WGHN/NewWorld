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
        ExternalIdentities: [],
        StoreIds: [Guid.Parse("0197a000-0000-7000-8000-000000000101"), Guid.Parse("0197a000-0000-7000-8000-000000000102")],
        EmployeeIds: [Guid.Parse("0197a000-0000-7000-8000-000000000001"), Guid.Parse("0197a000-0000-7000-8000-000000000002")],
        RoleAdditions: ["Public User", "Filialleitung"],
        Privileges:
        [
            new PrivilegeGrant("absence:read", false,
                [Guid.Parse("0197a000-0000-7000-8000-000000000101"), Guid.Parse("0197a000-0000-7000-8000-000000000102")],
                [Guid.Parse("0197a000-0000-7000-8000-000000000001"), Guid.Parse("0197a000-0000-7000-8000-000000000002")]),
            new PrivilegeGrant("faq:read", true, [], []),
        ]);

    public static readonly ResponsibilitiesChanged ResponsibilitiesChanged = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(2)), // a substitution starts at midnight
        EmployeeId: Guid.Parse("0197a000-0000-7000-8000-000000000002"),
        Version: 3,
        Responsibilities:
        [
            new TopicResponsibility("absence.approval", [Guid.Parse("0197a000-0000-7000-8000-000000000001")]),
            new TopicResponsibility("audit.instruction", [Guid.Parse("0197a000-0000-7000-8000-000000000009")]),
        ]);
}
