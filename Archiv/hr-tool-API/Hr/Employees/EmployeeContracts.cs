using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using HrTool_API.Common.Api;
using HrTool_API.Domain.Entities;

namespace HrTool_API.Hr;

/// <summary>Ein Mitarbeiter mit den Codes seiner Stammdaten (Arbeitsort, Rolle, Vertrag), damit die Oberfläche keine Nachschlagelisten braucht.</summary>
public record EmployeeResponse(
    Guid Id, string EmployeeNo, Guid? UserId, string FirstName, string LastName, string? Function, string? Email, string? Remark,
    Guid? WorkplaceId, string? WorkplaceCode, Guid? LocationId,
    Guid? PlanningRoleId, string? PlanningRoleCode,
    Guid? ContractId, string? ContractCode, decimal? WeeklyHours,
    string? RegionCode, bool ShiftSwapEnabled, bool Active, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static readonly Expression<Func<Employee, EmployeeResponse>> Projection = e => new EmployeeResponse(
        e.Id, e.EmployeeNo, e.UserId, e.FirstName, e.LastName, e.Function, e.Email, e.Remark,
        e.WorkplaceId, e.Workplace!.Code, e.Workplace.LocationId,
        e.PlanningRoleId, e.PlanningRole!.Code,
        e.ContractId, e.Contract!.Code, e.Contract.WeeklyHours,
        e.RegionCode, e.ShiftSwapEnabled, e.Active, e.CreatedAt, e.UpdatedAt);
}

public class EmployeeFilterRequest : PaginatedFilterRequest
{
    /// <summary>Teil des Namens oder der Personalnummer (Groß-/Kleinschreibung egal).</summary>
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Search { get; init; }

    public Guid? WorkplaceId { get; init; }

    /// <summary>Alle Arbeitsorte dieser Filiale (Rollen-Rechte).</summary>
    public Guid? LocationId { get; init; }

    public Guid? PlanningRoleId { get; init; }
    public bool? Active { get; init; }
}

/// <summary>
/// PATCH: fehlend/<c>null</c> = unverändert. Geändert wird nur, was HR selbst pflegt; Personalnummer, Name, E-Mail und Funktion kommen aus BC und stehen hier nicht.
/// Mitarbeiter werden nicht angelegt (Import aus BC) und nicht gelöscht, sondern deaktiviert.
/// </summary>
public record UpdateEmployeeRequest
{
    /// <summary>Login bei Rollen-Rechte (<c>sub</c>); je User höchstens ein Mitarbeiter.</summary>
    public Guid? UserId { get; init; }

    public Guid? WorkplaceId { get; init; }
    public Guid? PlanningRoleId { get; init; }
    public Guid? ContractId { get; init; }

    [MaxLength(20, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 20 Zeichen enthalten.")]
    public string? RegionCode { get; init; }

    public bool? ShiftSwapEnabled { get; init; }
    public string? Remark { get; init; }

    /// <summary><c>true</c> aktiviert einen deaktivierten Mitarbeiter wieder.</summary>
    public bool? Active { get; init; }
}
