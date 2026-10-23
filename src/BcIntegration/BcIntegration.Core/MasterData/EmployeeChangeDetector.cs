namespace BcIntegration.Core.MasterData;

public sealed record EmployeeChanges(bool Changed, bool Hired, bool Left)
{
    public static readonly EmployeeChanges None = new(false, false, false);
}

/// <summary>
/// Decides which events a new employee state causes. Hired/left are derived from entry and exit date:
/// a newly seen employee is hired (and left, if an exit date exists); a changed entry date means hired again;
/// a set or changed exit date means left. A removed exit date is only a change (no "rehired" message in the catalogue).
/// During the initial load (<c>lifecycleEvents</c> = false) no hired/left events are derived, so existing
/// staff is not announced as newly hired.
/// </summary>
public static class EmployeeChangeDetector
{
    public static EmployeeChanges Detect(EmployeeState? previous, EmployeeState current, bool lifecycleEvents)
    {
        if (previous == current)
        {
            return EmployeeChanges.None;
        }

        if (!lifecycleEvents)
        {
            return new EmployeeChanges(Changed: true, Hired: false, Left: false);
        }

        if (previous is null)
        {
            return new EmployeeChanges(Changed: true, Hired: current.EntryDate is not null, Left: current.ExitDate is not null);
        }

        return new EmployeeChanges(
            Changed: true,
            Hired: current.EntryDate is not null && current.EntryDate != previous.EntryDate,
            Left: current.ExitDate is not null && current.ExitDate != previous.ExitDate);
    }
}
