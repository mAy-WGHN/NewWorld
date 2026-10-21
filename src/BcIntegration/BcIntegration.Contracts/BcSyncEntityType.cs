using System.Text.Json.Serialization;

namespace BcIntegration.Contracts;

/// <summary>Kind of entity written to Business Central.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BcSyncEntityType>))]
public enum BcSyncEntityType
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Absence (sick note or vacation) from <c>SyncAbsenceToBc</c>.</summary>
    Absence = 1,
}
