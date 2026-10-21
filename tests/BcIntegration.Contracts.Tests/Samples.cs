using BcIntegration.Contracts;

namespace BcIntegration.Contracts.Tests;

/// <summary>One sample per message. Values cover edge cases (leap day, year end, DST change, nulls, empty lists).</summary>
internal static class Samples
{
    public static readonly Guid MessageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid EmployeeId = Guid.Parse("0197a000-0000-7000-8000-000000000001");
    public static readonly Guid StoreId = Guid.Parse("0197a000-0000-7000-8000-000000000101");

    // Night of the change to summer time in Germany (02:00 CET -> 03:00 CEST).
    public static readonly DateTimeOffset OccurredAt = new(2026, 3, 29, 3, 15, 0, TimeSpan.FromHours(2));

    public static readonly EmployeeChanged EmployeeChanged = new(
        MessageId, CorrelationId, OccurredAt,
        EmployeeId,
        PersonnelNumber: "10042",
        Name: "Erika Mustermann",
        HomeStoreId: StoreId,
        Position: "FILIALLEITUNG",
        EmploymentType: "TEILZEIT",
        ContractedHoursPerWeek: 30.5m,
        EntryDate: new DateOnly(2024, 2, 29),
        ExitDate: null);

    public static readonly EmployeeHired EmployeeHired = new(
        MessageId, CorrelationId, OccurredAt, EmployeeId, EntryDate: new DateOnly(2028, 2, 29), HomeStoreId: null);

    public static readonly EmployeeLeft EmployeeLeft = new(
        MessageId, CorrelationId, OccurredAt, EmployeeId, ExitDate: new DateOnly(2026, 12, 31));

    public static readonly EmployeeDataErasureRequested EmployeeDataErasureRequested = new(
        MessageId, CorrelationId, OccurredAt, EmployeeId);

    public static readonly StoreChanged StoreChanged = new(
        MessageId, CorrelationId, OccurredAt,
        StoreId,
        StoreNumber: "F017",
        Name: "Filiale Musterstadt",
        Address: new StoreAddress("Hauptstraße 1", "12345", "Musterstadt"),
        OpeningHours:
        [
            new StoreOpeningHours(Weekday.Monday, new TimeOnly(8, 0), new TimeOnly(20, 0)),
            new StoreOpeningHours(Weekday.Saturday, new TimeOnly(7, 30), new TimeOnly(18, 0)),
        ],
        IsActive: true);

    public static readonly BcSyncSucceeded BcSyncSucceeded = new(
        MessageId, CorrelationId, OccurredAt,
        SourceService.Workforce, BcSyncEntityType.Absence,
        EntityId: Guid.Parse("0197a000-0000-7000-8000-000000000201"),
        Version: 3,
        BcSystemId: Guid.Parse("8f2c1e4a-5b6d-4c3e-9a1b-2c3d4e5f6a7b"));

    public static readonly BcSyncFailed BcSyncFailed = new(
        MessageId, CorrelationId, OccurredAt,
        SourceService.Workforce, BcSyncEntityType.Absence,
        EntityId: Guid.Parse("0197a000-0000-7000-8000-000000000201"),
        Version: 4,
        ErrorCode: BcSyncErrorCode.ValidationFailed);
}
