using System.ComponentModel.DataAnnotations;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Contracts;

/// <summary>Ohne Passwort-Hash und ohne BC-PIN. Anwendungsspezifische Einstellungen (Benachrichtigungen, Push) liegen in der jeweiligen Anwendung.</summary>
public record UserResponse(
    Guid Id,
    string Username,
    bool Active,
    Guid? HomeLocationId,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static UserResponse From(User u) => new(u.Id, u.Username, u.Active, u.HomeLocationId, u.CreatedAt, u.UpdatedAt);
}

public record CreateUserRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(180, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 180 Zeichen enthalten.")]
    public string? Username { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MinLength(8, ErrorMessage = "Dieser Wert ist zu kurz. Er muss mindestens 8 Zeichen enthalten.")]
    public string? Password { get; init; }
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert.</summary>
public record UpdateUserRequest
{
    public bool? Active { get; init; }
}

public record ResetPasswordRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MinLength(8, ErrorMessage = "Dieser Wert ist zu kurz. Er muss mindestens 8 Zeichen enthalten.")]
    public string? Password { get; init; }
}

public record ChangePasswordRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public string? CurrentPassword { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MinLength(8, ErrorMessage = "Dieser Wert ist zu kurz. Er muss mindestens 8 Zeichen enthalten.")]
    public string? NewPassword { get; init; }
}

public class UserFilterRequest : PaginatedFilterRequest
{
    [AllowedValues("id", "username", "createdAt", "updatedAt", null, ErrorMessage = "Ungültiges Sortierfeld.")]
    public string? SortBy { get; init; }
}
