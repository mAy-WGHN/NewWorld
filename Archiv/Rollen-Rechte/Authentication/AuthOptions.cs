namespace Rollen_Rechte.Authentication;

/// <summary>Section "Auth": BC-Anmeldung (Auth:BcryptCost liest der PasswordService direkt).</summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Base64-codierter 32-Byte-Schlüssel für das BC-PIN (z. B. <c>openssl rand -base64 32</c>).</summary>
    public string? PinEncryptionKey { get; set; }

    /// <summary>
    /// true: ein BC-Mitarbeiter ohne IRS-User bekommt beim ersten erfolgreichen BC-Login automatisch einen.
    /// Der User hat zunächst keine Rolle und damit keine Rechte; die Zuweisung erfolgt von Hand.
    /// </summary>
    public bool BcAutoCreateUsers { get; set; } = true;
}
