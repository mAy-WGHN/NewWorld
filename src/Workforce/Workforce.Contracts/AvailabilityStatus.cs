using System.Text.Json.Serialization;

namespace Workforce.Contracts;

/// <summary>Availability without reason.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AvailabilityStatus>))]
public enum AvailabilityStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Available again (e.g. absence withdrawn or cancelled).</summary>
    Available = 1,

    /// <summary>Not available ("abwesend").</summary>
    Unavailable = 2,
}
