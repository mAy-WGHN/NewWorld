namespace Shared.Messaging;

/// <summary>
/// Sensitivity of a message (see "Datenschutz-Regeln"). When in doubt, use the stricter value.
/// Class 2 has two levels: neutralised content may go on topics; full content only to restricted receivers.
/// </summary>
public enum SensitivityClass
{
    /// <summary>Not classified. Architecture tests fail for messages with this value.</summary>
    Unknown = 0,

    /// <summary>Class 1: organisation data (store, personnel number, name, position, ...). Full state may go on the bus.</summary>
    OrganizationData = 1,

    /// <summary>Class 2, neutralised or thin: the sensitive fact is left out (e.g. "unavailable" without reason, IDs only).</summary>
    SensitiveNeutralized = 2,

    /// <summary>
    /// Class 2 with content (e.g. absence reason). Only in commands to a receiver with restricted access
    /// (<c>SyncAbsenceToBc</c>) or in-app only (<c>SendNotification</c>); never in events.
    /// </summary>
    SensitiveFacts = 3,

    /// <summary>Class 3: highly sensitive (salary, bank, tax, diagnoses). Never on the bus.</summary>
    HighlySensitive = 4,
}
