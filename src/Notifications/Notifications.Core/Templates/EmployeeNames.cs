namespace Notifications.Core.Templates;

/// <summary>
/// Splits the display name of an employee (from <c>EmployeeChanged.Name</c>) into first and last name for <c>$USER.firstName</c> and
/// <c>$USER.lastName</c>. TODO(BC-Modell): BC delivers only one name; the format is an assumption: "Vorname Nachname" (the first word is the first
/// name, the rest the last name) or "Nachname, Vorname". Replace by separate fields once BC Integration delivers them.
/// </summary>
public static class EmployeeNames
{
    public static RecipientName Split(string displayName)
    {
        var name = displayName.Trim();
        if (name.Contains(',', StringComparison.Ordinal))
        {
            var parts = name.Split(',', 2, StringSplitOptions.TrimEntries);
            return new RecipientName(parts[1], parts[0]);
        }

        var space = name.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? new RecipientName(name, string.Empty) : new RecipientName(name[..space], name[(space + 1)..].Trim());
    }
}
