using System.Security.Claims;
using Rollen_Rechte.Common.Api;

namespace Rollen_Rechte.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new ApiException(StatusCodes.Status401Unauthorized, "Kein angemeldeter Benutzer.");
}
