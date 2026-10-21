using Workforce.Contracts;

namespace Workforce.Contracts.Tests;

/// <summary>One sample per message. Values cover edge cases (year end, open end date, shift across midnight and DST change).</summary>
internal static class Samples
{
    public static readonly Guid MessageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid EmployeeId = Guid.Parse("0197a000-0000-7000-8000-000000000001");
    public static readonly DateTimeOffset OccurredAt = new(2026, 12, 31, 23, 59, 59, TimeSpan.FromHours(1));

    public static readonly EmployeeAvailabilityChanged EmployeeAvailabilityChanged = new(
        MessageId, CorrelationId, OccurredAt, EmployeeId,
        From: new DateOnly(2026, 12, 28),
        To: null,
        Status: AvailabilityStatus.Unavailable);

    // Night shift across midnight into the change to winter time (03:00 CEST -> 02:00 CET).
    public static readonly ShiftUncovered ShiftUncovered = new(
        MessageId, CorrelationId, OccurredAt,
        ShiftId: Guid.Parse("0197a000-0000-7000-8000-000000000301"),
        StoreId: Guid.Parse("0197a000-0000-7000-8000-000000000101"),
        From: new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.FromHours(2)),
        To: new DateTimeOffset(2026, 10, 25, 6, 0, 0, TimeSpan.FromHours(1)));

    public static readonly SyncAbsenceToBc SyncAbsenceToBc = new(
        MessageId, CorrelationId, OccurredAt,
        AbsenceId: Guid.Parse("0197a000-0000-7000-8000-000000000201"),
        EmployeeId: EmployeeId,
        From: new DateOnly(2026, 12, 28),
        To: new DateOnly(2027, 1, 4),
        Type: AbsenceType.Vacation,
        Status: AbsenceStatus.Approved,
        Version: 3);
}
