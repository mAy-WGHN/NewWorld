using System.ComponentModel.DataAnnotations;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Contracts;

/// <summary>Mitglied eines Scopes: Dimension ("location" = Filiale, "report" = Einzelmeldung) und ID des Objekts.</summary>
public record ScopeMemberDto(string Dimension, Guid RefId);

public record ScopeRef(Guid Id, string Name);

public record ScopeResponse(
    Guid Id, string Name, string? Description, IReadOnlyList<ScopeMemberDto> Members, int AssignmentCount, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static ScopeResponse From(Scope s, int assignmentCount) => new(
        s.Id, s.Name, s.Description,
        s.Members.OrderBy(m => m.Dimension).ThenBy(m => m.RefId).Select(m => new ScopeMemberDto(m.Dimension, m.RefId)).ToList(),
        assignmentCount, s.CreatedAt, s.UpdatedAt);
}

public record CreateScopeRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    public List<ScopeMemberDto>? Members { get; init; }
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert; <c>members</c> ersetzt die gesamte Mitgliederliste.</summary>
public record UpdateScopeRequest
{
    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    public List<ScopeMemberDto>? Members { get; init; }
}

/// <summary>Eine Rollenzuweisung mit Reichweite und Gültigkeit (<c>PUT /api/users/{id}/roles</c>).</summary>
public record AssignmentInput
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public Guid? RoleId { get; init; }

    /// <summary>false = uneingeschränkt; true = nur in <see cref="ScopeIds"/> (und ggf. der Heimatfiliale).</summary>
    public bool Scoped { get; init; }

    /// <summary>Nur mit <see cref="Scoped"/>: zusätzlich die Heimatfiliale des Users.</summary>
    public bool HomeScope { get; init; }

    public List<Guid>? ScopeIds { get; init; }

    /// <summary>Gültig ab / bis (Urlaubsvertretung, Zweitmeinung); null = sofort bzw. unbefristet. Abgelaufene Zuweisungen gelten nicht mehr.</summary>
    public DateTime? ValidFrom { get; init; }

    public DateTime? ValidTo { get; init; }
}

/// <summary>Setzt die Rollenzuweisungen eines Users komplett neu: entweder <c>assignments</c> (mit Scope/Gültigkeit) oder <c>roleIds</c> (uneingeschränkt, unbefristet).</summary>
public record SetUserRolesRequest
{
    public List<Guid>? RoleIds { get; init; }

    public List<AssignmentInput>? Assignments { get; init; }
}

public record AssignmentResponse(
    Guid Id, string Name, bool Scoped, bool HomeScope, IReadOnlyList<ScopeRef> Scopes, DateTime? ValidFrom, DateTime? ValidTo, bool Valid);

public record ScopeInfo(bool All, IReadOnlyList<Guid> LocationIds, IReadOnlyList<Guid> ReportIds);

public record SetHomeLocationRequest
{
    /// <summary>null = Heimatfiliale entfernen.</summary>
    public Guid? LocationId { get; init; }
}
