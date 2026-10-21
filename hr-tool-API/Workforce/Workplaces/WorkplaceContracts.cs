using System.Linq.Expressions;
using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

/// <summary>Ein Arbeitsort aus BC; <see cref="LocationId"/> ist die Filiale bei Rollen-Rechte (<c>null</c>, solange sie nicht aufgelöst ist oder es keine gibt).</summary>
public record WorkplaceResponse(
    Guid Id, string Code, string Name, string? StoreCode, string? CostCenterCode, string? RegionCode, bool TimeAccountActive, int VacationThreshold, Guid? LocationId)
{
    public static readonly Expression<Func<Workplace, WorkplaceResponse>> Projection =
        w => new WorkplaceResponse(w.Id, w.Code, w.Name, w.StoreCode, w.CostCenterCode, w.RegionCode, w.TimeAccountActive, w.VacationThreshold, w.LocationId);
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert.</summary>
public record UpdateWorkplaceRequest
{
    /// <summary>Filiale bei Rollen-Rechte. Normalerweise ergibt sie sich automatisch aus der Store-Nr.; von Hand nur, wo das nicht klappt.</summary>
    public Guid? LocationId { get; init; }
}
