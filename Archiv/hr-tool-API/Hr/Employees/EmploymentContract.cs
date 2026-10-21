namespace HrTool_API.Hr;

/// <summary>
/// Beschäftigungsverhältnis (Spalte „Beschäftigungsverhältniscode“, z. B. <c>TZ 15,25H</c>, <c>MINI 6,5H</c>, <c>AZU 40H</c>). Der Code ist uneinheitlich geschrieben
/// („TZ 21,25“, „TZ 25,50 H“); die Wochenstunden stehen im Code und werden beim Import herausgelesen. Die Soll-Stunden einer Periode ergeben sich daraus
/// (Wochenstunden × Tage / 7), sie werden nicht gespeichert.
/// </summary>
public class EmploymentContract
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Code aus BC, eindeutig.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Vertragliche Wochenstunden; <c>null</c>, wenn sie sich aus dem Code nicht lesen lassen.</summary>
    public decimal? WeeklyHours { get; set; }
}
