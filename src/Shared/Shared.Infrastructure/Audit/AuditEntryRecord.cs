namespace Shared.Infrastructure.Audit;

/// <summary>What was done to a record.</summary>
public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
}

/// <summary>Who acted: a signed-in person, a calling service or nobody known (public endpoints).</summary>
public enum AuditActorKind
{
    Person = 1,
    Service = 2,
    Anonymous = 3,
}

/// <summary>
/// One line of the audit log of a service (append only: the interceptor refuses to change or delete it and a database trigger enforces the same).
/// The actor's name and personnel number are copied in on purpose, so the line stays readable when the person changes or leaves.
/// <see cref="ChangesJson"/> holds the changed values (property → value, or old/new for updates); only people with the "read values" privilege see it.
/// </summary>
public sealed class AuditEntryRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Time of the action (from <see cref="TimeProvider"/>), never changed.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    public AuditActorKind ActorKind { get; init; }
    public Guid? ActorEmployeeId { get; init; }
    public string ActorPersonnelNumber { get; init; } = string.Empty;
    public string ActorName { get; init; } = string.Empty;

    public AuditAction Action { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;

    public string? ChangesJson { get; init; }
}
