namespace Portal.Core.CashRegisters;

/// <summary>What happened to a cash register (the history of a store). Stored as text.</summary>
public enum CashRegisterEventKind
{
    /// <summary>A register was recorded for the first time.</summary>
    Recorded = 1,

    /// <summary>A register was edited and recorded again (name or store); this removes the seal.</summary>
    Changed = 2,

    /// <summary>The seal of a register was renewed.</summary>
    Sealed = 3,

    /// <summary>A register was deleted.</summary>
    Deleted = 4,
}
