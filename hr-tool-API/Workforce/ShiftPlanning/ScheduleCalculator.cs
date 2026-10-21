namespace HrTool_API.Workforce;

/// <summary>
/// Die Stundenrechnung des Dienstplans, abgeleitet aus einem fertigen Dienstplan (BC-Export, Monatsmatrix):
/// <list type="bullet">
/// <item>Eine Schicht zählt mit Zeiten abzüglich der Pause; Schichtcode ohne Zeiten zählt nicht (die Schichtdefinition dahinter ist nicht bekannt).
///   Liegt das Ende vor dem Beginn, geht die Schicht über Mitternacht (<c>16-0</c> = 8 h).</item>
/// <item>Pause: über 6 h 30 min (an 10 von 10 Mitarbeitern des Exports nachgerechnet). Über 9 h gilt die gesetzliche Mindestpause von 45 min (§ 4 ArbZG);
///   Schichten über 9 h kommen im Export nicht vor, dieser Wert ist also nicht an BC geprüft.</item>
/// <item>Eine Nicht-Verfügbarkeit <b>mit</b> Zeiten (<c>URLAUB 14-20</c>) zählt als geplant, <b>ohne</b> Zeiten (<c>U</c>, <c>FT</c>) nicht.</item>
/// <item>Soll = Wochenstunden × Tage der Periode / 7 (30 h/Woche im 31-Tage-Monat = 132,86 h).</item>
/// </list>
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>Länge zwischen Beginn und Ende in Stunden; ein Ende vor dem Beginn geht über Mitternacht, gleiche Zeiten ergeben 0.</summary>
    public static decimal GrossHours(TimeOnly start, TimeOnly end)
    {
        var minutes = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        if (minutes < 0) minutes += 24 * 60;
        return (decimal)minutes / 60m;
    }

    public static decimal BreakHours(decimal grossHours) => grossHours > 9m ? 0.75m : grossHours > 6m ? 0.5m : 0m;

    public static decimal NetHours(TimeOnly start, TimeOnly end)
    {
        var gross = GrossHours(start, end);
        return gross - BreakHours(gross);
    }

    /// <summary>Die Stunden, die eine Schicht im Plan zählt (0 ohne Zeiten).</summary>
    public static decimal CountedHours(TimeOnly? start, TimeOnly? end) => start is { } s && end is { } e ? NetHours(s, e) : 0m;

    public static decimal TargetHours(decimal weeklyHours, int days) => Math.Round(weeklyHours * days / 7m, 2);

    /// <summary>Geplant in Prozent vom Soll; <c>null</c>, wenn es kein Soll gibt.</summary>
    public static decimal? PlannedPercent(decimal planned, decimal? target) =>
        target is > 0m ? Math.Round(planned / target.Value * 100m, 2) : null;
}
