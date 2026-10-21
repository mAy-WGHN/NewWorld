using System.Globalization;
using System.Text.RegularExpressions;

namespace HrTool_API.Services.Import;

/// <summary>Umwandlungen der BC-Schreibweisen, die der Import braucht.</summary>
public static partial class ImportParsers
{
    private static readonly string[] Months =
        ["januar", "februar", "märz", "april", "mai", "juni", "juli", "august", "september", "oktober", "november", "dezember"];

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*H?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WeeklyHoursRegex();

    [GeneratedRegex(@"^(\p{L}+)\s+(\d{4})$")]
    private static partial Regex MonthNameRegex();

    /// <summary>
    /// Wochenstunden aus dem Code des Beschäftigungsverhältnisses: <c>TZ 15,25H</c> → 15,25; <c>MINI 9,00H</c> → 9; <c>AZU 40H</c> → 40; <c>TZ 21,25</c> und <c>TZ 25,50 H</c> gehen ebenfalls.
    /// <c>null</c> bei Codes ohne Zahl (<c>VERKAUF</c>).
    /// </summary>
    public static decimal? ParseWeeklyHours(string code)
    {
        var match = WeeklyHoursRegex().Match(code.Trim());
        return match.Success && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) ? hours : null;
    }

    /// <summary>„April  2021“ (auch mit mehreren Leerzeichen) → Jahr und Monat; <c>null</c>, wenn es kein Monatsname ist.</summary>
    public static (int Year, int Month)? ParseMonthName(string text)
    {
        var match = MonthNameRegex().Match(string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        if (!match.Success) return null;
        var month = Array.IndexOf(Months, match.Groups[1].Value.ToLowerInvariant()) + 1;
        return month == 0 ? null : (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), month);
    }

    public static ScheduleStatus? ParseScheduleStatus(string text) => text.Trim().ToLowerInvariant() switch
    {
        "entwurf" => ScheduleStatus.Draft,
        "bestätigt" => ScheduleStatus.Confirmed,
        _ => null
    };

    public static ShiftPatternKind? ParseShiftPatternKind(string text) => text.Trim().ToLowerInvariant() switch
    {
        "tagesmuster" => ShiftPatternKind.Day,
        "wochenmuster" => ShiftPatternKind.Week,
        _ => null
    };

    /// <summary>Zahl in der Schreibweise der Exporte (Punkt als Dezimalzeichen); leer = 0.</summary>
    public static decimal ParseNumber(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0m : decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
}
