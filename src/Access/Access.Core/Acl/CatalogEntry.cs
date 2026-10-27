namespace Access.Core.Acl;

/// <summary>A registered privilege. Only registered privileges take effect.</summary>
/// <param name="Key">Privilege <c>resource:action</c>.</param>
/// <param name="Owner">Application that registered it (e.g. <c>workforce</c>), <c>access</c>, or <c>custom</c> for own privileges.</param>
/// <param name="Scopable">Can be restricted to stores; otherwise it takes effect only in unrestricted assignments.</param>
public sealed record CatalogEntry(string Key, string Owner, bool Scopable);

/// <summary>The privileges of the Access service itself (administration of the ACL).</summary>
public static class AccessPrivileges
{
    public const string Owner = "access";

    public const string RoleRead = "access:role:read";
    public const string RoleCreate = "access:role:create";
    public const string RoleUpdate = "access:role:update";
    public const string RoleDelete = "access:role:delete";
    public const string ScopeRead = "access:scope:read";
    public const string ScopeCreate = "access:scope:create";
    public const string ScopeUpdate = "access:scope:update";
    public const string ScopeDelete = "access:scope:delete";
    public const string AssignmentRead = "access:assignment:read";
    public const string AssignmentCreate = "access:assignment:create";
    public const string AssignmentDelete = "access:assignment:delete";
    public const string ResponsibilityRead = "access:responsibility:read";
    public const string ResponsibilityCreate = "access:responsibility:create";
    public const string ResponsibilityDelete = "access:responsibility:delete";
    public const string CatalogRead = "access:catalog:read";
    public const string CustomPrivilegeCreate = "access:custom-privilege:create";
    public const string CustomPrivilegeDelete = "access:custom-privilege:delete";
    public const string PersonRead = "access:person:read";
    public const string MainStoreUpdate = "access:main-store:update";

    public static readonly IReadOnlyList<CatalogEntry> Catalog =
    [
        new(RoleRead, Owner, false), new(RoleCreate, Owner, false), new(RoleUpdate, Owner, false), new(RoleDelete, Owner, false),
        new(ScopeRead, Owner, false), new(ScopeCreate, Owner, false), new(ScopeUpdate, Owner, true), new(ScopeDelete, Owner, false),
        new(AssignmentRead, Owner, true), new(AssignmentCreate, Owner, true), new(AssignmentDelete, Owner, true),
        new(ResponsibilityRead, Owner, true), new(ResponsibilityCreate, Owner, true), new(ResponsibilityDelete, Owner, true),
        new(CatalogRead, Owner, false), new(CustomPrivilegeCreate, Owner, false), new(CustomPrivilegeDelete, Owner, false),
        new(PersonRead, Owner, true), new(MainStoreUpdate, Owner, true),
    ];
}
