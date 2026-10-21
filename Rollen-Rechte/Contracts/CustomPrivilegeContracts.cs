using System.ComponentModel.DataAnnotations;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Contracts;

public record CustomPrivilegeResponse(Guid Id, string Key, string Label, string? Description, int RoleCount, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static CustomPrivilegeResponse From(CustomPrivilege c, int roleCount) =>
        new(c.Id, c.Key, c.Label, c.Description, roleCount, c.CreatedAt, c.UpdatedAt);
}

public record CreateCustomPrivilegeRequest
{
    /// <summary>Format <c>bereich:aktion</c> in Kleinbuchstaben, z. B. <c>report:export</c>; danach nicht mehr änderbar.</summary>
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(100, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 100 Zeichen enthalten.")]
    public string? Key { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Label { get; init; }

    public string? Description { get; init; }
}

/// <summary>PATCH: nur Label und Beschreibung; der Schlüssel ist unveränderlich (Rollen verweisen darauf).</summary>
public record UpdateCustomPrivilegeRequest
{
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Label { get; init; }

    public string? Description { get; init; }
}
