namespace BC_Anbindung.BusinessCentral;

/// <summary>
/// Konfiguration der Server-zu-Server-Anbindung an Business Central (Section "BusinessCentral"):
/// OAuth2-Client-Credentials gegen Azure AD sowie die Adressierung der BC-Online-API.
/// Die Werte gehören nicht ins Repo — lokal per <c>.env</c>, produktiv per Umgebungsvariable.
/// Ohne ClientId/ClientSecret/Scope/TenantId/Environment/CompanyName ist die Anbindung abgeschaltet (die Endpunkte antworten dann mit 503).
/// </summary>
public class BcAuthOptions
{
    public const string SectionName = "BusinessCentral";

    /// <summary>Optional; Default ist der v2.0-Endpunkt des <see cref="TenantId"/>.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;

    /// <summary>Azure-AD-Tenant-ID; adressiert zusammen mit <see cref="Environment"/> die BC-Online-API.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>BC-Umgebungsname, Teil der API-Basis-URL.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>BC-Company-Kurzname, wie in der Service-URL "WS/{Company}/Codeunit/..." adressiert.</summary>
    public string CompanyName { get; set; } = string.Empty;


    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(Scope) && !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(Environment) && !string.IsNullOrWhiteSpace(CompanyName);

    public string ResolvedTokenEndpoint => string.IsNullOrWhiteSpace(TokenEndpoint)
        ? $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token"
        : TokenEndpoint;
}
