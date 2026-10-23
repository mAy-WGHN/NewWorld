namespace BcIntegration.Core.MasterData;

public sealed class MasterDataSyncOptions
{
    public const string ConfigurationSection = "Bc:MasterData";

    /// <summary>
    /// How often master data is compared with BC. TODO(Entscheidung): webhooks vs. polling and the interval are open;
    /// polling is the default, a webhook can trigger the same run.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Re-read window before the watermark, against clock skew and records saved during the last run.</summary>
    public TimeSpan Overlap { get; set; } = TimeSpan.FromMinutes(5);
}
