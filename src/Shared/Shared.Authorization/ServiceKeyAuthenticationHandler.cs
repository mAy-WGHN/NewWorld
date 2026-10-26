using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Authorization;

/// <summary>
/// Authenticates a calling service from its signed token (see <see cref="ServiceTokenValidator"/>). The principal carries the service
/// name and exactly the service roles configured for that peer; the policies <see cref="AppPolicies.ServiceRole"/> check them.
/// </summary>
public sealed partial class ServiceKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder, ServiceTokenValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!validator.IsServiceToken(header))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await validator.ValidateAsync(ServiceTokenValidator.BearerToken(header)!);
        if (!result.Succeeded)
        {
            LogRejected(Logger, result.Failure ?? "unknown");
            return AuthenticateResult.Fail("Invalid service token.");
        }

        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(AppClaimTypes.ServiceName, result.Service!));
        foreach (var role in result.Roles)
        {
            identity.AddClaim(new Claim(AppClaimTypes.ServiceRole, role));
        }

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    // Reason code only: never the token, never its content.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Service token rejected ({Reason})")]
    private static partial void LogRejected(ILogger logger, string reason);
}

/// <summary>Adds a token of this service to calls to <paramref name="targetService"/> (a fresh one per call, also per retry).</summary>
public sealed class ServiceTokenHandler(ServiceTokenIssuer issuer, string targetService) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.Create(targetService));
        return base.SendAsync(request, cancellationToken);
    }
}
