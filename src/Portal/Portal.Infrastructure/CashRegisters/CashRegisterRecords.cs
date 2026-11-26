using Portal.Core.CashRegisters;

namespace Portal.Infrastructure.CashRegisters;

/// <summary>
/// A cash register of a store. TODO(Fachlogik): "sealed" is the state of the drawer, a plain flag (<see cref="IsSealed"/>) that is set explicitly
/// (seal renewed or lifted, or ticked when recording or editing).
/// </summary>
public sealed class CashRegisterRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid StoreId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset LastRecordedAt { get; set; }
    public Guid LastRecordedBy { get; set; }

    /// <summary>The state of the drawer: sealed or not, a plain flag. A new register starts not sealed.</summary>
    public bool IsSealed { get; set; }

    /// <summary>When and by whom the state was last set (sealed, seal renewed or lifted; for a new register the recording).</summary>
    public DateTimeOffset StateChangedAt { get; set; }

    public Guid StateChangedBy { get; set; }

    /// <summary>The form of the current seal: the photo and who attached the seal (what a person says in the form; only while sealed). The time is <see cref="StateChangedAt"/>.</summary>
    public Guid? SealPhotoId { get; set; }

    public Guid? SealAttachedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Sets the state and remembers when and by whom (the one place that changes it). A seal comes with its photo and the person who attached it; lifting clears both.</summary>
    public void SetSealed(bool sealedState, DateTimeOffset at, Guid by, Guid? photoId = null, Guid? attachedBy = null)
    {
        IsSealed = sealedState;
        StateChangedAt = at;
        StateChangedBy = by;
        SealPhotoId = sealedState ? photoId : null;
        SealAttachedBy = sealedState ? attachedBy : null;
    }
}

/// <summary>A photo of a seal, uploaded before the seal is set; it belongs to the person who uploaded it until a seal uses it (then <see cref="UsedAt"/> is set).</summary>
public sealed class CashRegisterPhotoRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Extension { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
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

    /// <summary>For a seal: the photo and who attached it (from the form).</summary>
    public Guid? PhotoId { get; set; }

    public Guid? AttachedBy { get; set; }
}
