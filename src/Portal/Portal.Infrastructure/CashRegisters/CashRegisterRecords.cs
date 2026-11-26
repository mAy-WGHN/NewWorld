using Portal.Core.CashRegisters;

namespace Portal.Infrastructure.CashRegisters;

/// <summary>
/// A cash register of a store. TODO(Fachlogik): the meaning of "sealed" is derived from the list of the Kassenregister: recording (or editing) a register
/// removes its seal, renewing the seal sets it. The status is <see cref="SealedAt"/> being set.
/// </summary>
public sealed class CashRegisterRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid StoreId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset LastRecordedAt { get; set; }
    public Guid LastRecordedBy { get; set; }
    public DateTimeOffset? SealedAt { get; set; }
    public Guid? SealedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The one row with the settings of the daily reminder (maintained by the holders of <c>cashregister:manage-reminders</c>).</summary>
public sealed class CashRegisterReminderSettingsRecord
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public TimeOnly Time { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
}

/// <summary>A local day for which the reminder went out; one row per day, so a repeated round sends nothing twice.</summary>
public sealed class CashRegisterReminderRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateOnly Day { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public int Notifications { get; set; }
}

/// <summary>One line of the history of a store. The register is kept by name, because the register itself can be deleted.</summary>
public sealed class CashRegisterEventRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid StoreId { get; set; }
    public Guid RegisterId { get; set; }
    public required string RegisterName { get; set; }
    public CashRegisterEventKind Kind { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid By { get; set; }
}
