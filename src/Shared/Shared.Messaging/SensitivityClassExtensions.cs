namespace Shared.Messaging;

public static class SensitivityClassExtensions
{
    /// <summary>Class number of the privacy rules (1, 2 or 3).</summary>
    public static int PrivacyClass(this SensitivityClass sensitivity) => sensitivity switch
    {
        SensitivityClass.OrganizationData => 1,
        SensitivityClass.SensitiveNeutralized or SensitivityClass.SensitiveFacts => 2,
        SensitivityClass.HighlySensitive => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(sensitivity), sensitivity, "Message is not classified."),
    };
}
