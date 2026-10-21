using System.Text.Json.Serialization;

namespace Workforce.Contracts;

/// <summary>Kind of absence. No diagnoses or medical details exist anywhere.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AbsenceType>))]
public enum AbsenceType
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Krankmeldung.</summary>
    Sickness = 1,

    /// <summary>Urlaub.</summary>
    Vacation = 2,
}
