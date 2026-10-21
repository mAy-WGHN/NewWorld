using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Testing;

public sealed class TestAuthOptions : AuthenticationSchemeOptions;

/// <summary>
/// Replaces Entra in tests. Identity and roles come per request from headers and are emitted as provider
/// claims (<c>oid</c>, <c>roles</c>), so the real claims mapping runs. Scopes are set in the permission cache.
/// No header → anonymous.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<TestAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<TestAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string ObjectIdHeader = "X-Test-Oid";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hasOid = Request.Headers.TryGetValue(ObjectIdHeader, out var oid);
        var hasRoles = Request.Headers.TryGetValue(RolesHeader, out var roles);
        if (!hasOid && !hasRoles)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>();
        if (hasOid)
        {
            claims.Add(new Claim("oid", oid.ToString()));
        }

        if (hasRoles)
        {
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => new Claim("roles", r)));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
