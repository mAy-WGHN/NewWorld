namespace Workforce.Core.Absences;

/// <summary>A period of calendar days, both ends included. <see cref="To"/> = <c>null</c> means "until further notice" (open end).</summary>
public readonly record struct DateRange(DateOnly From, DateOnly? To)
{
    public bool IsOpen => To is null;

    public static DateRange Of(DateOnly from, DateOnly to) =>
        to < from ? throw new ArgumentException("The end of a period cannot lie before its start.", nameof(to)) : new DateRange(from, to);

    public bool Contains(DateOnly date) => date >= From && (To is null || date <= To);

    /// <summary>Two periods overlap if they share at least one day; an open end reaches into the future without limit.</summary>
    public bool Overlaps(DateRange other) =>
        (To is null || other.From <= To) && (other.To is null || From <= other.To);
}
