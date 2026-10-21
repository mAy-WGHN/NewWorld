using Shared.Messaging;

namespace BcIntegration.Contracts;

/// <summary>Full current state of a store (BC master data). Published only on a real change.</summary>
/// <remarks>Publisher: BC Integration. Consumers: Workforce, Portal, Access.</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the change detection (TimeProvider).</param>
/// <param name="StoreId">System-wide internal ID, issued by BC Integration.</param>
/// <param name="StoreNumber">Filialnummer as in BC (LSC Store <c>No.</c>).</param>
/// <param name="Name">Store name.</param>
/// <param name="Address">Postal address. TODO(BC-Modell): address fields of the LSC store.</param>
/// <param name="OpeningHours">Regular opening hours per weekday; empty if unknown. TODO(BC-Modell): source in BC.</param>
/// <param name="IsActive">Whether the store is operating.</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record StoreChanged(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid StoreId,
    string StoreNumber,
    string Name,
    StoreAddress? Address,
    IReadOnlyList<StoreOpeningHours> OpeningHours,
    bool IsActive) : IEvent;

/// <summary>Postal address of a store.</summary>
/// <param name="Street">Street and house number.</param>
/// <param name="PostalCode">Postleitzahl.</param>
/// <param name="City">City.</param>
public sealed record StoreAddress(string Street, string PostalCode, string City);

/// <summary>Opening hours of one weekday (local store time).</summary>
/// <param name="Day">Weekday.</param>
/// <param name="Opens">Opening time.</param>
/// <param name="Closes">Closing time.</param>
public sealed record StoreOpeningHours(Weekday Day, TimeOnly Opens, TimeOnly Closes);
