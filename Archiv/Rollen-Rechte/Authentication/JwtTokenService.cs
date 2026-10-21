using System.Security.Cryptography;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Rollen_Rechte.Authentication;

public record TokenPair(string Token, string RefreshToken);

public class JwtTokenService(IOptions<JwtSettings> options, JwtKeys keys, AccessDbContext db, TimeProvider clock)
{
    private readonly JwtSettings _settings = options.Value;

    /// <summary>
    /// Claims wie bei Lexik: iat, exp und username (ohne roles: Rechte kommen aus /api/me/privileges). Das Angular-Frontend dekodiert den Payload selbst
    /// und braucht genau exp und username (zusätzlich steht die User-ID als sub im Token).
    /// </summary>
    public string CreateAccessToken(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["username"] = user.Username,
                // Die User-ID: Anwendungen ohne eigene Benutzer-Tabelle (z. B. Benachrichtigungen) erkennen den User daran, ohne nachzufragen.
                ["sub"] = user.Id.ToString()
            },
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = now,
            NotBefore = null,
            Expires = now.AddDays(_settings.AccessTokenDays),
            SigningCredentials = new SigningCredentials(keys.SigningKey, keys.Algorithm)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Stellt Access- und neues Refresh-Token aus.</summary>
    public async Task<TokenPair> IssueAsync(User user, CancellationToken ct)
    {
        var refresh = new RefreshToken
        {
            Token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(64)),
            Username = user.Username,
            Valid = clock.GetUtcNow().UtcDateTime.AddDays(_settings.RefreshTokenDays)
        };
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        return new TokenPair(CreateAccessToken(user), refresh.Token);
    }

    /// <summary>
    /// Refresh mit Verlängerung bei Nutzung (ttl_update): gleiches Refresh-Token, neue Laufzeit.
    /// Gibt null zurück, wenn Token unbekannt/abgelaufen oder der User deaktiviert ist.
    /// </summary>
    public async Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == refreshToken, ct);
        if (stored is null || stored.Valid < now) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == stored.Username && u.Active, ct);
        if (user is null) return null;

        stored.Valid = now.AddDays(_settings.RefreshTokenDays);
        await db.SaveChangesAsync(ct);
        return new TokenPair(CreateAccessToken(user), stored.Token);
    }
}
