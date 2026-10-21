using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using HrTool_API.Common.Api;
using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

// --- Nichtverfügbarkeitsarten --------------------------------------------------------------------------------

public record AbsenceTypeResponse(Guid Id, string Code, string Description, bool PaidTime, string? ScheduleSymbol, string? SetWorkCode, bool RequiresApproval)
{
    public static readonly Expression<Func<AbsenceType, AbsenceTypeResponse>> Projection =
        t => new AbsenceTypeResponse(t.Id, t.Code, t.Description, t.PaidTime, t.ScheduleSymbol, t.SetWorkCode, t.RequiresApproval);
}

public record CreateAbsenceTypeRequest
{
    /// <summary>Code wie in BC (Großbuchstaben), eindeutig und danach unveränderlich.</summary>
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(40, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 40 Zeichen enthalten.")]
    public string? Code { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Description { get; init; }

    public bool PaidTime { get; init; }

    /// <summary>Kürzel im Dienstplan (<c>U</c>, <c>K</c> …).</summary>
    [MaxLength(10, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 10 Zeichen enthalten.")]
    public string? ScheduleSymbol { get; init; }

    [MaxLength(20, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 20 Zeichen enthalten.")]
    public string? SetWorkCode { get; init; }

    /// <summary>Muss die Art genehmigt werden (Urlaubsanträge)?</summary>
    public bool RequiresApproval { get; init; }
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert; der Code ändert sich nie.</summary>
public record UpdateAbsenceTypeRequest
{
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Description { get; init; }

    public bool? PaidTime { get; init; }

    [MaxLength(10, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 10 Zeichen enthalten.")]
    public string? ScheduleSymbol { get; init; }

    [MaxLength(20, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 20 Zeichen enthalten.")]
    public string? SetWorkCode { get; init; }

    public bool? RequiresApproval { get; init; }
}
