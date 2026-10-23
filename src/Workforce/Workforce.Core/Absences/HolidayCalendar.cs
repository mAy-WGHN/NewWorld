namespace Workforce.Core.Absences;

/// <summary>Statutory holidays, replaceable (e.g. by a calendar fed from another source). A state code is the two-letter ISO 3166-2:DE code.</summary>
public interface IHolidayCalendar
{
    bool IsHoliday(DateOnly date, string? state);
}

/// <summary>
/// German statutory holidays: the nationwide ones always, state-specific ones when a state is given. Without a state only the nationwide
/// holidays count. TODO(Entscheidung): which state applies (per store? one default?) is open; Mariä Himmelfahrt is only modelled for
/// Saarland (in Bavaria it depends on the municipality), Augsburger Friedensfest and similar local holidays are not modelled.
/// </summary>
public sealed class GermanHolidayCalendar : IHolidayCalendar
{
    private static readonly HashSet<string> States = new(StringComparer.Ordinal)
    {
        "BW", "BY", "BE", "BB", "HB", "HH", "HE", "MV", "NI", "NW", "RP", "SL", "SN", "ST", "SH", "TH",
    };

    private static readonly string[] Epiphany = ["BW", "BY", "ST"];
    private static readonly string[] WomensDay = ["BE", "MV"];
    private static readonly string[] CorpusChristi = ["BW", "BY", "HE", "NW", "RP", "SL"];
    private static readonly string[] Assumption = ["SL"];
    private static readonly string[] WorldChildrensDay = ["TH"];
    private static readonly string[] ReformationDay = ["BB", "HB", "HH", "MV", "NI", "SN", "ST", "SH", "TH"];
    private static readonly string[] AllSaints = ["BW", "BY", "NW", "RP", "SL"];
    private static readonly string[] RepentanceDay = ["SN"];

    public bool IsHoliday(DateOnly date, string? state)
    {
        var code = Normalize(state);
        var easter = EasterSunday(date.Year);

        // nationwide
        if ((date.Month, date.Day) is (1, 1) or (5, 1) or (10, 3) or (12, 25) or (12, 26))
        {
            return true;
        }

        if (date == easter.AddDays(-2) || date == easter.AddDays(1) || date == easter.AddDays(39) || date == easter.AddDays(50))
        {
            return true;
        }

        if (code is null)
        {
            return false;
        }

        return ((date.Month, date.Day) switch
        {
            (1, 6) => Epiphany.Contains(code),
            (3, 8) => WomensDay.Contains(code),
            (8, 15) => Assumption.Contains(code),
            (9, 20) => WorldChildrensDay.Contains(code),
            (10, 31) => ReformationDay.Contains(code),
            (11, 1) => AllSaints.Contains(code),
            _ => false,
        })
        || (date == easter.AddDays(60) && CorpusChristi.Contains(code))
        || (RepentanceDay.Contains(code) && date == RepentanceAndPrayerDay(date.Year));
    }

    /// <summary>Easter Sunday (Gregorian calendar, "Anonymous Gregorian algorithm").</summary>
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    /// <summary>Buß- und Bettag: the Wednesday before 23 November.</summary>
    private static DateOnly RepentanceAndPrayerDay(int year)
    {
        var date = new DateOnly(year, 11, 22);
        while (date.DayOfWeek != DayOfWeek.Wednesday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    private static string? Normalize(string? state)
    {
        if (state is null)
        {
            return null;
        }

        var code = state.Trim().ToUpperInvariant();
        return States.Contains(code) ? code : throw new ArgumentException($"Unknown state code '{state}'.", nameof(state));
    }
}
