namespace Benachrichtigungen.Domain.Enums;

public enum ReportSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public static class ReportSeverityExtensions
{
    /// <summary>Höher = dringlicher. Für die Sortierung nach Prio (nicht alphabetisch nach String).</summary>
    public static int Rank(this ReportSeverity severity) => severity switch
    {
        ReportSeverity.Low => 1,
        ReportSeverity.Medium => 2,
        ReportSeverity.High => 3,
        ReportSeverity.Critical => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
    };
}
