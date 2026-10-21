using System.ComponentModel.DataAnnotations;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Contracts;

public record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Privileges,
    bool BuiltIn,
    int UserCount,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static RoleResponse From(AclRole r, int userCount) => new(
        r.Id, r.Name, r.Description, r.Privileges.ToList(), AclGuard.IsBuiltIn(r.Name), userCount, r.CreatedAt, r.UpdatedAt);
}

public record RoleSummary(Guid Id, string Name);

public record CreateRoleRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Rechte aus dem Katalog (<c>GET /api/privileges</c>), <c>*</c> oder <c>bereich:*</c>.</summary>
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public List<string>? Privileges { get; init; }
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert; <c>privileges</c> ersetzt die gesamte Liste.</summary>
public record UpdateRoleRequest
{
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    public List<string>? Privileges { get; init; }
}

public record PrivilegeActionResponse(string Privilege, string Action, string Label, bool Custom, bool Scopable);

public record PrivilegeAreaResponse(string Key, string Label, IReadOnlyList<PrivilegeActionResponse> Actions);

/// <summary>
/// Die Rechte des angemeldeten Users für die UI: konkrete Katalog-Rechte (Platzhalter aufgelöst), je Recht die Reichweite (<c>scopes</c>, damit die UI z. B.
/// nur die eigenen Filialen anbietet) und die gültigen Rollenzuweisungen.
/// </summary>
public record MyPrivilegesResponse(IReadOnlyList<string> Privileges, IReadOnlyDictionary<string, ScopeInfo> Scopes, IReadOnlyList<AssignmentResponse> Roles);
