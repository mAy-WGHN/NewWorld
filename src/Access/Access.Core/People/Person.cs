namespace Access.Core.People;

/// <summary>
/// Read-only copy of a person from BC (via <c>EmployeeChanged</c>/<c>EmployeeLeft</c>). The exit date is the last working day;
/// permissions end at the end of this day, local time.
/// </summary>
public sealed record Person(Guid EmployeeId, string PersonnelNumber, string Name, Guid? HomeStoreId, string? Position, DateOnly? ExitDate)
{
    /// <summary>Active until the end of the exit date in <paramref name="zone"/> (e.g. Europe/Berlin), not from receipt of the exit.</summary>
    public bool IsActiveAt(DateTimeOffset at, TimeZoneInfo zone) => ExitDate is not { } exit || at < LocalDayEnd(exit, zone);

    /// <summary>Start of the following local day as an instant (handles summer/winter time).</summary>
    public static DateTimeOffset LocalDayEnd(DateOnly date, TimeZoneInfo zone)
    {
        var nextMidnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(nextMidnight))
        {
            nextMidnight = nextMidnight.AddHours(1);
        }

        return new DateTimeOffset(nextMidnight, zone.GetUtcOffset(nextMidnight));
    }
}
