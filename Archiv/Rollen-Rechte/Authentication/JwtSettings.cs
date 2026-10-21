namespace Rollen_Rechte.Authentication;

public class JwtSettings
{
    /// <summary>
    /// RSA-Private-Key als PEM (Inhalt; "\n" als Literal ist erlaubt) oder Dateipfad. Mit diesem Schlüsselpaar
    /// (RS256, wie Lexik) bleiben bereits ausgestellte Tokens des Symfony-Backends gültig.
    /// </summary>
    public string? PrivateKey { get; set; }
    public string? PrivateKeyPath { get; set; }

    /// <summary>RSA-Public-Key (PEM-Inhalt oder Pfad); optional, sonst wird er aus dem Private-Key abgeleitet.</summary>
    public string? PublicKey { get; set; }
    public string? PublicKeyPath { get; set; }

    /// <summary>Passphrase des Private-Keys (PKCS#8, "BEGIN ENCRYPTED PRIVATE KEY"). Secret, nie ins Repo.</summary>
    public string? Passphrase { get; set; }

    /// <summary>Fallback ohne RSA (HS256), z. B. für lokale Entwicklung: mind. 32 Zeichen.</summary>
    public string? Key { get; set; }

    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public int AccessTokenDays { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}
