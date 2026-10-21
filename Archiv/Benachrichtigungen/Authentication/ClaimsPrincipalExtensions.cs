using System.Security.Claims;
using Benachrichtigungen.Common.Api;

namespace Benachrichtigungen.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new ApiException(StatusCodes.Status401Unauthorized, "Kein angemeldeter Benutzer.");
}
