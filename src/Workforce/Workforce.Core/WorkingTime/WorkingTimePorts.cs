namespace Workforce.Core.WorkingTime;

/// <summary>Bookings, month closes, rules and monthly thresholds. Changes are saved by the caller (one unit of work).</summary>
public interface IWorkingTimeStore
{
    Task<IReadOnlyList<TimeBooking>> BookingsAsync(Guid employeeId, CancellationToken cancellationToken);

    void AddBooking(TimeBooking booking);

    Task<IReadOnlyList<MonthClose>> ClosesAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<MonthClose?> CloseAsync(Guid employeeId, int year, int month, CancellationToken cancellationToken);

    void AddClose(MonthClose close);

    /// <summary>The rules HR set, or <c>null</c> if none were set (then the defaults apply).</summary>
    Task<WorkingTimeRulesSetting?> GetRulesAsync(CancellationToken cancellationToken);

    Task SetRulesAsync(WorkingTimeRulesSetting setting, CancellationToken cancellationToken);

    Task<IReadOnlyList<MonthlyThreshold>> ThresholdsAsync(CancellationToken cancellationToken);

    Task<decimal?> ThresholdAsync(Guid relationshipId, CancellationToken cancellationToken);

    /// <summary>
    /// People who left before <paramref name="beforeDay"/> (their exit date is over), have a working time account (at least one booking) and have not
    /// been looked at for the payout reminder yet.
    /// </summary>
    Task<IReadOnlyList<ExitedAccount>> ExitedAccountsToCheckAsync(DateOnly beforeDay, CancellationToken cancellationToken);

    /// <summary>Notes that the exit of a person was looked at (<paramref name="reminded"/>: HR was reminded to pay out a balance).</summary>
    void AddPayoutCheck(Guid employeeId, DateTimeOffset at, bool reminded);

    /// <summary>Sets the monthly threshold of an employment relationship; <c>null</c> removes it.</summary>
    Task SetThresholdAsync(Guid relationshipId, decimal? hours, Guid changedBy, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary>The employment relationship of a person (from the employee card) and the selection list it comes from.</summary>
public interface IEmploymentLookup
{
    /// <summary>The employment relationship on the card of the person; <c>null</c> if the person has no card or none is set.</summary>
    Task<Guid?> RelationshipOfAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>Whether the entry exists in the selection list "Arbeitsverhältnis".</summary>
    Task<bool> IsRelationshipAsync(Guid entryId, CancellationToken cancellationToken);
}
