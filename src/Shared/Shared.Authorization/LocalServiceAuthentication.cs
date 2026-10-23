using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Authorization;

/// <summary>
/// Stands in for Entra service tokens in the local landscape (AppHost): the static token of <see cref="TokenCredentialFactory.LocalFake"/>
/// counts as a calling service with all roles this API accepts. Only with <c>LocalServiceAuth=true</c> and only in Development/Testing.
/// </summary>
public static class LocalServiceAuthentication
{
    public const string SchemeName = "LocalService";
    public const string ConfigurationKey = "LocalServiceAuth";
    public const string StaticToken = "local-fake-token";

    /// <summary>True if the local service authentication is switched on (throws outside Development/Testing).</summary>
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>(ConfigurationKey))
        {
            return false;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("The local service authentication is only allowed in Development/Testing.");
        }

        return true;
    }

    public static AuthenticationBuilder AddLocalServiceScheme(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, LocalServiceHandler>(SchemeName, _ => { });

    /// <summary>True if the request carries the static local service token.</summary>
    public static bool HasStaticToken(string authorizationHeader) =>
        string.Equals(authorizationHeader.Trim(), "Bearer " + StaticToken, StringComparison.Ordinal);

    private sealed class LocalServiceHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SharedAuthorizationOptions roles)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!HasStaticToken(Request.Headers.Authorization.ToString()))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(roles.ServiceRoles.Select(r => new Claim("roles", r)), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
