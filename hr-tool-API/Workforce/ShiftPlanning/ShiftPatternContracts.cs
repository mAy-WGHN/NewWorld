using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using HrTool_API.Common.Api;
using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

// --- Schichtmuster -------------------------------------------------------------------------------------------

public record ShiftPatternResponse(Guid Id, string Code, string? Description, ShiftPatternKind Kind, int WeekCount)
{
    public static readonly Expression<Func<ShiftPattern, ShiftPatternResponse>> Projection =
        p => new ShiftPatternResponse(p.Id, p.Code, p.Description, p.Kind, p.WeekCount);
}

public class ShiftPatternFilterRequest : PaginatedFilterRequest
{
    /// <summary>Teil des Codes oder der Beschreibung (Groß-/Kleinschreibung egal).</summary>
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Search { get; init; }

    public ShiftPatternKind? Kind { get; init; }
}

public record CreateShiftPatternRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(30, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 30 Zeichen enthalten.")]
    public string? Code { get; init; }

    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Description { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public ShiftPatternKind? Kind { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [Range(0, 52, ErrorMessage = "Die Anzahl der Wochen muss zwischen 0 und 52 liegen.")]
    public int? WeekCount { get; init; }
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert; der Code ändert sich nie.</summary>
public record UpdateShiftPatternRequest
{
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Description { get; init; }

    public ShiftPatternKind? Kind { get; init; }

    [Range(0, 52, ErrorMessage = "Die Anzahl der Wochen muss zwischen 0 und 52 liegen.")]
    public int? WeekCount { get; init; }
}
