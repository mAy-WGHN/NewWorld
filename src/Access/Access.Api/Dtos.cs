namespace Access.Api;

public sealed record MainStoreDto(Guid? EffectiveStoreId, Guid? OverrideStoreId, Guid? SuggestedStoreId);

public sealed record SetMainStoreRequest(Guid StoreId);

public sealed record PrivilegeDto(string Key, bool Unrestricted, IReadOnlyList<Guid> StoreIds, IReadOnlyList<Guid> EmployeeIds);

public sealed record MyPrivilegesDto(Guid EmployeeId, IReadOnlyList<string> Roles, IReadOnlyList<PrivilegeDto> Privileges);

public sealed record CatalogEntryDto(string Key, string Owner, bool Scopable, string Description = "");

public sealed record RoleDto(Guid Id, string Name, IReadOnlyList<string> Privileges, bool BuiltIn);

public sealed record CreateRoleRequest(string Name, IReadOnlyList<string> Privileges);

public sealed record UpdateRoleRequest(IReadOnlyList<string> Privileges);

public sealed record ScopeDto(Guid Id, string Name, IReadOnlyList<Guid> StoreIds);

public sealed record CreateScopeRequest(string Name, IReadOnlyList<Guid> StoreIds);

public sealed record UpdateScopeRequest(IReadOnlyList<Guid> StoreIds);

public sealed record AssignmentDto(
    Guid Id, Guid EmployeeId, Guid RoleId, bool Unrestricted, IReadOnlyList<Guid> ScopeIds, bool IncludeHomeStore,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, string Source);

public sealed record CreateAssignmentRequest(
    Guid RoleId, bool Unrestricted, IReadOnlyList<Guid>? ScopeIds, bool IncludeHomeStore, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo);

public sealed record ResponsibilityTargetDto(string Kind, Guid Id);

public sealed record CreateResponsibilityRequest(
    string Topic, Guid ResponsibleEmployeeId, ResponsibilityTargetDto Target, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo);

public sealed record TopicResponsibilityDto(string Topic, IReadOnlyList<Guid> ResponsibleEmployeeIds);

public sealed record CreateCustomPrivilegeRequest(string Key, bool Scopable, string? Description = null);

public sealed record RegisterCatalogRequest(IReadOnlyList<RegisterCatalogEntry> Privileges);

public sealed record RegisterCatalogEntry(string Key, bool Scopable, string? Description = null);

/// <summary>Current state of one person for rebuilding a copy; apply with the usual version check.</summary>
public sealed record PermissionStateDto(Guid EmployeeId, long Version, IReadOnlyList<PrivilegeDto> Privileges);

public sealed record ResponsibilityStateDto(Guid EmployeeId, long Version, IReadOnlyList<TopicResponsibilityDto> Responsibilities);

public sealed record CreatedDto(Guid Id);

public sealed record LoginRequest(string PersonnelNumber, string Pin);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>Tokens of a session: a short-lived access token and a single-use refresh token.</summary>
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);
