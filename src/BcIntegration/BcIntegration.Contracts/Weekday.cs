using System.Text.Json.Serialization;

namespace BcIntegration.Contracts;

/// <summary>Weekday (ISO numbering). Own enum because <see cref="DayOfWeek"/> has no <c>Unknown = 0</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Weekday>))]
public enum Weekday
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Montag.</summary>
    Monday = 1,

    /// <summary>Dienstag.</summary>
    Tuesday = 2,

    /// <summary>Mittwoch.</summary>
    Wednesday = 3,

    /// <summary>Donnerstag.</summary>
    Thursday = 4,

    /// <summary>Freitag.</summary>
    Friday = 5,

    /// <summary>Samstag.</summary>
    Saturday = 6,

    /// <summary>Sonntag.</summary>
    Sunday = 7,
}
