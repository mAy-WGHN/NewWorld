namespace Workforce.Core.WorkingTime;

public enum BookingKind
{
    Unknown = 0,

    /// <summary>The balance HR enters when the account starts (plus or minus); once per person.</summary>
    OpeningBalance = 1,

    /// <summary>Overtime of a closed month (plus).</summary>
    Overtime = 2,

    /// <summary>Compensatory time off (minus).</summary>
    TimeOff = 3,

    /// <summary>Overtime paid out (minus).</summary>
    Payout = 4,

    /// <summary>The remaining balance paid out when the person leaves (minus).</summary>
    PayoutAtExit = 5,

    /// <summary>A later change, e.g. after wrong time tracking (plus or minus).</summary>
    Correction = 6,
}

/// <summary>
/// One booking in the working time account of a person. <see cref="Hours"/> is signed: plus raises, minus lowers the balance. <see cref="Date"/> is the day
/// it counts for; overtime carries the date it was earned (end of its month), which decides when it expires.
/// </summary>
public sealed class TimeBooking
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public BookingKind Kind { get; set; }

    public decimal Hours { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Overtime only: the month close it comes from.</summary>
    public Guid? MonthCloseId { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The close of one month for one person: the hours worked (entered by HR until the time tracking exists), the monthly threshold of the employment
/// relationship at that time, and the overtime that resulted (hours above the threshold, 1:1).
/// </summary>
public sealed class MonthClose
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public decimal WorkedHours { get; set; }

    public decimal ThresholdHours { get; set; }

    public decimal OvertimeHours { get; set; }

    public Guid ClosedBy { get; set; }

    public DateTimeOffset ClosedAt { get; set; }
}

/// <summary>
/// The rules HR sets for everybody (not per employment relationship): payout threshold in hours (default 0 = always) and the expiry of overtime
/// in months after the earning date (default none).
/// </summary>
public sealed record WorkingTimeRules(decimal PayoutThresholdHours, int? ExpiryMonths)
{
    public static readonly WorkingTimeRules Default = new(0, null);

    public bool IsValid => PayoutThresholdHours is >= 0 and <= 1000 && (ExpiryMonths is null || ExpiryMonths is >= 1 and <= 120);
}

public sealed record WorkingTimeRulesSetting(WorkingTimeRules Rules, DateTimeOffset ChangedAt, Guid ChangedBy);

/// <summary>The monthly threshold (hours per month) of an employment relationship, from which on hours count as overtime.</summary>
public sealed record MonthlyThreshold(Guid RelationshipId, decimal Hours);

public enum WorkingTimeError
{
    None = 0,
    UnknownPerson = 1,
    NotFound = 2,

    /// <summary>Hours are zero, too large or have more than two decimals.</summary>
    InvalidHours = 3,

    /// <summary>The date or month is not sensible.</summary>
    InvalidPeriod = 4,

    /// <summary>The month is not over yet.</summary>
    MonthNotFinished = 5,
    AlreadyClosed = 6,

    /// <summary>The person has no employment relationship on the card, so no monthly threshold applies.</summary>
    NoEmploymentRelationship = 7,

    /// <summary>HR has not set a monthly threshold for the employment relationship.</summary>
    NoThreshold = 8,

    /// <summary>The balance does not cover the hours.</summary>
    InsufficientBalance = 9,

    /// <summary>The balance is below the payout threshold HR set.</summary>
    BelowPayoutThreshold = 10,
    OpeningBalanceExists = 11,

    /// <summary>The person has no exit date.</summary>
    NotLeaving = 12,

    NothingToPay = 13,
    InvalidRules = 14,

    /// <summary>The entry is not an employment relationship of the selection list.</summary>
    UnknownRelationship = 15,
}

public sealed record BookingResult(WorkingTimeError Error, TimeBooking? Booking = null);

public sealed record MonthCloseResult(WorkingTimeError Error, MonthClose? Close = null, TimeBooking? Booking = null);
