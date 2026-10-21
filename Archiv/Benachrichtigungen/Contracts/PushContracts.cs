using System.ComponentModel.DataAnnotations;

namespace Benachrichtigungen.Contracts;

public record PushKeys
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public string? P256dh { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public string? Auth { get; init; }
}

public record CreatePushSubscriptionRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [Url(ErrorMessage = "Dieser Wert ist keine gültige URL.")]
    public string? Endpoint { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public PushKeys? Keys { get; init; }
}

public record DeletePushSubscriptionRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [Url(ErrorMessage = "Dieser Wert ist keine gültige URL.")]
    public string? Endpoint { get; init; }
}

public record PushPublicKeyResponse(string PublicKey);
