using Microsoft.AspNetCore.Authorization;

namespace Shared.Authorization;

/// <summary>Resource for a privilege check on a store.</summary>
public sealed record StoreResource(Guid StoreId);

/// <summary>Resource for a privilege check on a person.</summary>
public sealed record EmployeeResource(Guid EmployeeId);

/// <summary>Requires the ACL privilege <see cref="Key"/>, optionally for a resource.</summary>
public sealed class PrivilegeRequirement(string key) : IAuthorizationRequirement
{
    public string Key { get; } = key;
}
