namespace Access.Infrastructure.Acl;

public sealed class CatalogEntryRecord
{
    public required string Key { get; set; }
    public required string Owner { get; set; }
    public bool Scopable { get; set; }
    public string Description { get; set; } = "";
}

public sealed class RoleRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public List<string> Privileges { get; set; } = [];
    public bool BuiltIn { get; set; }
}

public sealed class ScopeRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public List<Guid> StoreIds { get; set; } = [];
}

public sealed class AssignmentRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid RoleId { get; set; }
    public bool Unrestricted { get; set; }
    public bool IncludeHomeStore { get; set; }
    public List<Guid> ScopeIds { get; set; } = [];
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public required string Source { get; set; }
}

public sealed class ResponsibilityRecord
{
    public Guid Id { get; set; }
    public required string Topic { get; set; }
    public Guid ResponsibleEmployeeId { get; set; }

    /// <summary><c>Employee</c>, <c>Store</c> or <c>Scope</c>.</summary>
    public required string TargetKind { get; set; }

    public Guid TargetId { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}
