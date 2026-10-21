using HrTool_API.Authentication;

namespace HrTool_API.Workforce;

/// <summary>Nichtverfügbarkeitsart (Export „Nichtverfügbarkeitsarten“, 24 Arten, z. B. <c>URLAUB</c>, <c>KRANK</c>, <c>MUTTERSCHUTZ</c>).</summary>
[PrivilegeResource("Nichtverfügbarkeitsarten", DefinesReach = false)]
public class AbsenceType
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Code aus BC, eindeutig (Großbuchstaben, auch mit Leerzeichen, z. B. <c>KRANK I URLAUB</c>).</summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>„Bezahlte Zeit“: Zählt die Zeit als bezahlt (z. B. Mutterschutz, Prüfung)?</summary>
    public bool PaidTime { get; set; }

    /// <summary>
    /// Ob die Art genehmigt werden muss: nur <b>Urlaub</b> (<c>URLAUB</c>). Alle anderen Arten pflegt nur HR im Dienstplan ein und sie gelten automatisch als genehmigt. Muss beim Import der Arten gesetzt werden,
    /// die Liste aus BC enthält die Angabe nicht.
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>„Zeitplan Zeichen“: Kürzel im Dienstplan (<c>U</c>, <c>K</c>, <c>MS</c>); bei einzelnen Arten leer.</summary>
    public string? ScheduleSymbol { get; set; }

    /// <summary>„Arbeitscode setzen“; im Export durchgehend leer.</summary>
    public string? SetWorkCode { get; set; }
}
