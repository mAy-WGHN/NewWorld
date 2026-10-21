using System.Text.Json.Serialization;

namespace BcIntegration.Contracts;

/// <summary>Service that requested a BC write; used as subscription filter for the sync results.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SourceService>))]
public enum SourceService
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Workforce API (absences).</summary>
    Workforce = 1,
}
