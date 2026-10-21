using System.ComponentModel.DataAnnotations;

namespace Rollen_Rechte.Contracts;

/// <summary>Ein Recht einer Anwendung im Format <c>bereich:aktion</c>, z. B. <c>report:read</c>.</summary>
public record AppPrivilegeInput
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Privilege { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Label { get; init; }

    /// <summary>Darf das Recht auf einen Scope beschränkt vergeben werden (die Anwendung wendet ihn in ihren Abfragen an)?</summary>
    public bool Scopable { get; init; }
}

public record AppAreaInput
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(50, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 50 Zeichen enthalten.")]
    public string? Key { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Label { get; init; }

    /// <summary>false bei reinen Verwaltungsbereichen: Dort bestimmt der Scope nicht, wie weit jemand "reicht".</summary>
    public bool DefinesReach { get; init; } = true;

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public List<AppPrivilegeInput>? Actions { get; init; }
}

/// <summary>Der vollständige Rechtekatalog einer Anwendung: ersetzt bei jeder Registrierung den bisherigen dieser Anwendung.</summary>
public record RegisterCatalogRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public List<AppAreaInput>? Areas { get; init; }
}

public record RegisterCatalogResponse(string AppKey, int Privileges);

/// <summary>
/// "Wer darf <c>privilege</c> in Bezug auf diese Filialen/Einzelobjekte?" — z. B. <c>report:read</c> mit den Filialen einer neuen Meldung. Ohne Filialen und Einzelobjekte
/// zählen nur uneingeschränkte Zuweisungen. <c>userIds</c> schränkt die Kandidaten ein (z. B. auf User mit Push-Abo).
/// </summary>
public record ResolveRecipientsRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Privilege { get; init; }

    public List<Guid>? LocationIds { get; init; }

    public List<Guid>? ReportIds { get; init; }

    public List<Guid>? UserIds { get; init; }
}

public record ResolveRecipientsResponse(IReadOnlyList<Guid> UserIds);
