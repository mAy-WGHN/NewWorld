using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Common.RateLimiting;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Controllers;

[Route("api")]
[AllowAnonymous]
public class AuthController(
    AccessDbContext db, JwtTokenService tokens, PasswordService passwords, LoginThrottle throttle, BcLoginService bcLogin)
    : ApiControllerBase
{
    // Bewusst identisch für falsches Passwort, unbekannten User, deaktivierten Account und Throttling.
    private const string LoginFailed = "Benutzername oder Passwort ist falsch.";

    [HttpPost("auth/login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(request.Password)) return LoginUnauthorized();
        if (!throttle.TryAcquire(username, RateLimitingSetup.ClientIp(HttpContext))) return LoginUnauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is { Active: false })
        {
            passwords.Verify(request.Password, null); // gleiche Antwortzeit wie bei einer echten Prüfung
            return LoginUnauthorized();
        }

        // BC zuerst (Personalnummer + PIN); lokal gilt das Passwort nur, wenn BC nicht gefragt wurde, nicht erreichbar
        // ist oder den User nicht kennt. Ein BC-geführter User wird von BC dagegen endgültig abgelehnt, damit ein dort
        // geändertes oder entzogenes PIN nicht über den lokalen Stand weiter funktioniert.
        var bcResult = await bcLogin.TryLoginAsync(username, request.Password, user, ct);
        switch (bcResult)
        {
            case { Outcome: BcLoginOutcome.Authenticated }:
                user = bcResult.User!;
                break;
            case { Outcome: BcLoginOutcome.Rejected } when user?.BcPinEncrypted is not null:
                return LoginUnauthorized();
            default:
                var passwordOk = passwords.Verify(request.Password, user?.Password);
                if (user is null || !passwordOk) return LoginUnauthorized();
                break;
        }

        var pair = await tokens.IssueAsync(user, ct);
        return Ok(new TokenResponse(pair.Token, pair.RefreshToken));
    }

    [HttpPost("token/refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var pair = string.IsNullOrEmpty(request.RefreshToken)
            ? null
            : await tokens.RefreshAsync(request.RefreshToken, ct);

        return pair is null
            ? StatusCode(StatusCodes.Status401Unauthorized, new ErrorResponse("Ungültiges oder abgelaufenes Refresh-Token.", []))
            : Ok(new TokenResponse(pair.Token, pair.RefreshToken));
    }

    private ObjectResult LoginUnauthorized() =>
        StatusCode(StatusCodes.Status401Unauthorized, new ErrorResponse(LoginFailed, []));
}
