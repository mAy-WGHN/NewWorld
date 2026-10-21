using System.ComponentModel.DataAnnotations;

namespace BC_Anbindung.Contracts;

public record VerifyEmployeeRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(50, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 50 Zeichen enthalten.")]
    public string? EmployeeNo { get; init; }

    /// <summary>Das PIN. Ein leeres PIN ist erlaubt und ergibt immer "nicht authentifiziert" — es wird nie an BC gegeben (BC würde es als gültig werten).</summary>
    [Required(AllowEmptyStrings = true, ErrorMessage = "Dieser Wert muss angegeben werden.")]
    public string? Pin { get; init; }
}

/// <summary>Ergebnis der Prüfung. "Authenticated = false" heißt: BC kennt Personalnummer/PIN nicht. Ist BC nicht erreichbar, antwortet der Dienst mit 503.</summary>
public record VerifyEmployeeResponse(bool Authenticated, string? EmployeeName);

public record StoreResponse(string StoreNo, string Name);
