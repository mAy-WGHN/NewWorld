using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Contracts;

public record LocationResponse(Guid Id, string Name, string StoreCode, bool Archived)
{
    public static readonly Expression<Func<Location, LocationResponse>> Projection =
        l => new LocationResponse(l.Id, l.Name, l.StoreCode, l.Archived);

    public static LocationResponse From(Location l) => new(l.Id, l.Name, l.StoreCode, l.Archived);
}

public record CreateLocationRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(180, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 180 Zeichen enthalten.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(10, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 10 Zeichen enthalten.")]
    public string? StoreCode { get; init; }
}

public record UpdateLocationRequest
{
    [MaxLength(180, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 180 Zeichen enthalten.")]
    public string? Name { get; init; }

    [MaxLength(10, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 10 Zeichen enthalten.")]
    public string? StoreCode { get; init; }

    public bool? Archived { get; init; }
}

public class LocationFilterRequest : PaginatedFilterRequest
{
    public string? Search { get; init; }

    [AllowedValues("id", "name", "storeCode", "archived", null, ErrorMessage = "Ungültiges Sortierfeld.")]
    public string? SortBy { get; init; }
}
