using System.Text.Json.Serialization;

namespace Workforce.Contracts;

/// <summary>Status of an absence. Sick notes need no approval and start as <see cref="Reported"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AbsenceStatus>))]
public enum AbsenceStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Vacation requested (beantragt).</summary>
    Requested = 1,

    /// <summary>Vacation approved (genehmigt).</summary>
    Approved = 2,

    /// <summary>Vacation rejected (abgelehnt).</summary>
    Rejected = 3,

    /// <summary>Withdrawn by the requester (zurückgezogen).</summary>
    Withdrawn = 4,

    /// <summary>Cancelled, e.g. vacation days replaced by sickness (storniert).</summary>
    Cancelled = 5,

    /// <summary>Sick note recorded (erfasst); no approval.</summary>
    Reported = 6,
}
