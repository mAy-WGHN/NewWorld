using System.Text.Json.Serialization;

namespace Rollen_Rechte.Contracts;

public record LoginRequest
{
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public record RefreshRequest
{
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }
}

/// <summary>Login/Refresh antworten bewusst ohne Envelope (Format des bisherigen Lexik-Bundles).</summary>
public record TokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("refresh_token")] string RefreshToken);
