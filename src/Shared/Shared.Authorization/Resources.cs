using Microsoft.AspNetCore.Authorization;

namespace Shared.Authorization;

/// <summary>"The user may access store X." Pass to <c>IAuthorizationService</c> with <see cref="AppPolicies.CanAccessStore"/>.</summary>
public sealed record StoreResource(Guid StoreId);

/// <summary>"The user may access employee Y." Pass with <see cref="AppPolicies.CanAccessEmployee"/>.</summary>
public sealed record EmployeeResource(Guid EmployeeId);

public sealed class StoreAccessRequirement : IAuthorizationRequirement;

public sealed class EmployeeAccessRequirement : IAuthorizationRequirement;
