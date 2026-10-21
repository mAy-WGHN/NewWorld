namespace Benachrichtigungen.Domain.Entities;

/// <summary>Ein Browser/Gerät eines Users, das Web Push empfängt. Der User gehört dem Dienst "Rollen-Rechte"; hier steht nur seine ID.</summary>
public class PushSubscription
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>ID des Users (JWT-Claim <c>sub</c>), kein Fremdschlüssel.</summary>
    public Guid UserId { get; set; }

    /// <summary>Eindeutig; Push-Service-URL.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>p256dh des Browsers.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>auth-Secret des Browsers.</summary>
    public string AuthToken { get; set; } = string.Empty;

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
}
