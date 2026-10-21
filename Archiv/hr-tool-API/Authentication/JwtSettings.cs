namespace HrTool_API.Authentication;

/// <summary>Nur zum Prüfen der Tokens von Rollen-Rechte (Issuer/Audience setzt dort niemand).</summary>
public class JwtSettings
{
    /// <summary>RSA-Public-Key als PEM (Inhalt; "\n" als Literal ist erlaubt) oder Dateipfad.</summary>
    public string? PublicKey { get; set; }
    public string? PublicKeyPath { get; set; }

    /// <summary>Fallback ohne RSA (HS256), z. B. für lokale Entwicklung: mind. 32 Zeichen, gleicher Wert wie bei Rollen-Rechte.</summary>
    public string? Key { get; set; }
}
