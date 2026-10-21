namespace Shared.Messaging;

/// <summary>
/// Sensitivity class of a message (see "Datenschutz-Regeln"). When in doubt, use the stricter class.
/// </summary>
public enum SensitivityClass
{
    /// <summary>Not classified. Architecture tests fail for messages with this value.</summary>
    Unknown = 0,

    /// <summary>Class 1: organisation data (store, personnel number, name, position, ...). Full state may go on the bus.</summary>
    OrganizationData = 1,

    /// <summary>Class 2: sensitive facts (absence reason, time corrections, ...). Only neutralised or thin (IDs only).</summary>
    SensitiveFacts = 2,

    /// <summary>Class 3: highly sensitive (salary, bank, tax, diagnoses). Never on the bus.</summary>
    HighlySensitive = 3,
}
