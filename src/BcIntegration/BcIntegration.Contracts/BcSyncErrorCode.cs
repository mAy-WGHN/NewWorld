using System.Text.Json.Serialization;

namespace BcIntegration.Contracts;

/// <summary>
/// Permanent business error of a BC write. A code only, so no content from BC or personal data can travel with it.
/// TODO(BC-Modell): refine once BC's validation error format is known.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BcSyncErrorCode>))]
public enum BcSyncErrorCode
{
    /// <summary>Not classified.</summary>
    Unknown = 0,

    /// <summary>BC rejected the data (validation).</summary>
    ValidationFailed = 1,

    /// <summary>A referenced record (e.g. the employee) does not exist in BC.</summary>
    ReferencedRecordMissing = 2,
}
