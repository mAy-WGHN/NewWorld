using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Authorization;

namespace Shared.Testing;

public sealed class TestAuthOptions : AuthenticationSchemeOptions;

/// <summary>
/// Replaces real tokens in tests. Per request from headers: a person (<c>employee_id</c> claim as in tokens issued by Access)
/// or a calling service (service role claims as the service authentication sets them). The real claims mapping runs; privileges are set in
/// the permission cache (<see cref="TestAuthentication.Grant"/>). No header → anonymous.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<TestAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<TestAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string EmployeeHeader = "X-Test-Employee";
    public const string ServiceRolesHeader = "X-Test-Service-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>();
        if (Request.Headers.TryGetValue(EmployeeHeader, out var employee))
        {
            claims.Add(new Claim("employee_id", employee.ToString()));
        }

        if (Request.Headers.TryGetValue(ServiceRolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => new Claim(AppClaimTypes.ServiceRole, r)));
        }

        if (claims.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
