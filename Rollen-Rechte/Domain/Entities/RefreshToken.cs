namespace Rollen_Rechte.Domain.Entities;

public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>Eindeutig.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Verweis auf users.username, bewusst ohne FK.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Ablaufzeitpunkt.</summary>
    public DateTime Valid { get; set; }

    /// <summary>Token-Familie (Rotation).</summary>
    public string? Family { get; set; }

    public DateTime? FamilyValid { get; set; }
}
