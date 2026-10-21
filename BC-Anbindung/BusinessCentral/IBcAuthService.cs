namespace BC_Anbindung.BusinessCentral;

/// <summary>
/// Stellt Access-Tokens für die Kommunikation mit Business Central bereit (OAuth2 Client-Credentials
/// gegen Azure AD, s. <see cref="BcAuthOptions"/>). Das Token wird bis kurz vor Ablauf gecacht.
/// </summary>
public interface IBcAuthService
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}
