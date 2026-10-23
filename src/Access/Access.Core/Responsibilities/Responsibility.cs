using System.Text.RegularExpressions;

namespace Access.Core.Responsibilities;

/// <summary>Whom a responsibility applies to: one person, everyone with this home store, or everyone in a scope.</summary>
public abstract record ResponsibilityTarget
{
    public sealed record Employee(Guid EmployeeId) : ResponsibilityTarget;

    public sealed record Store(Guid StoreId) : ResponsibilityTarget;

    public sealed record Scope(Guid ScopeId) : ResponsibilityTarget;

    public static ResponsibilityTarget ForEmployee(Guid employeeId) => new Employee(employeeId);

    public static ResponsibilityTarget ForStore(Guid storeId) => new Store(storeId);

    public static ResponsibilityTarget ForScope(Guid scopeId) => new Scope(scopeId);
}

/// <summary>
/// A manually maintained responsibility per topic (e.g. store manager for <c>absence.approval</c>, auditor for
/// <c>audit.instruction</c>), optionally time-limited. Organisational fact for approvals and notifications, not a permission.
/// </summary>
public sealed partial record Responsibility(
    Guid Id,
    string Topic,
    Guid ResponsibleEmployeeId,
    ResponsibilityTarget Target,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo)
{
    [GeneratedRegex("^[a-z0-9-]+(\\.[a-z0-9-]+)*$")]
    private static partial Regex TopicFormat();

    public static bool IsValidTopic(string topic) => TopicFormat().IsMatch(topic);

    public bool IsValidAt(DateTimeOffset at) => (ValidFrom is null || ValidFrom <= at) && (ValidTo is null || at < ValidTo);

    public static Responsibility Create(string topic, Guid responsible, ResponsibilityTarget target, DateTimeOffset? validFrom = null, DateTimeOffset? validTo = null) =>
        new(Guid.NewGuid(), topic, responsible, target, validFrom, validTo);
}
