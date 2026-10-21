using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Controllers;

[Route("api/me")]
[Authorize(Policy = AuthPolicies.Authenticated)]
public class MeController(AccessDbContext db, PasswordService passwords) : ApiControllerBase
{
    // Rechte und Rollen des angemeldeten Users für die UI (Menüs/Buttons ein- und ausblenden). Bewusst nicht im JWT:
    // Dort wären sie bis zum Ablauf veraltet. Platzhalter sind aufgelöst, die UI prüft nur auf konkrete Schlüssel.
    [HttpGet("privileges")]
    public async Task<IActionResult> Privileges(PrivilegeService privileges, PrivilegeCatalogService catalog, TimeProvider clock, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var snapshot = await catalog.GetAsync(ct);
        var scopes = new SortedDictionary<string, ScopeInfo>(StringComparer.Ordinal);
        foreach (var privilege in snapshot.All)
        {
            if (await privileges.ScopeForAsync(userId, privilege, ct) is not { } scope) continue;
            scopes[privilege] = new ScopeInfo(scope.All, scope.Locations.Order().ToList(), scope.Reports.Order().ToList());
        }

        var roles = await AssignmentReader.LoadAsync(db, userId, clock.GetUtcNow().UtcDateTime, onlyValid: true, ct);
        return Success(new MyPrivilegesResponse(scopes.Keys.ToList(), scopes, roles));
    }
    [HttpPatch("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw ApiException.BadRequest("Kein angemeldeter Benutzer.");

        if (!passwords.Verify(request.CurrentPassword!, user.Password))
            throw ApiException.BadRequest("Aktuelles Passwort ist falsch.");

        user.Password = passwords.Hash(request.NewPassword!);
        await db.SaveChangesAsync(ct);
        return SuccessEmpty();
    }
}
