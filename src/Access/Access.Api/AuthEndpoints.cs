using System.Security.Claims;
using Access.Core.Login;
using Access.Core.StepUp;
using Access.Infrastructure.Login;
using Access.Infrastructure.StepUp;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;

namespace Access.Api;

/// <summary>
/// Login with personnel number and BC PIN, session refresh and the public key (discovery + JWKS) that all other services use to validate
/// tokens locally. Answers never say why a login failed beyond what the caller needs; the PIN is not logged anywhere.
/// </summary>
public static class AuthEndpoints
{
    private static readonly string[] Rs256 = ["RS256"];
    private static readonly string[] Public = ["public"];
    private static readonly string[] Token = ["token"];

    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/login", async (LoginRequest request, HttpContext http, LoginService login, SessionService sessions, AccessTokenIssuer issuer, CancellationToken ct) =>
            {
                var result = await login.LoginAsync(request.PersonnelNumber ?? "", request.Pin ?? "", ClientKey(http), ct);
                switch (result.Status)
                {
                    case LoginStatus.Success:
                        return Results.Ok(await Tokens(result.EmployeeId!.Value, sessions, issuer, ct));
                    case LoginStatus.Locked:
                        var seconds = (result.LockedUntil!.Value - http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()).TotalSeconds;
                        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(seconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "TooManyAttempts");
                    case LoginStatus.Unavailable:
                        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "LoginUnavailable");
                    default:
                        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "InvalidCredentials");
                }
            })
            .AllowAnonymous();

        auth.MapPost("/refresh", async (RefreshRequest request, SessionService sessions, AccessTokenIssuer issuer, CancellationToken ct) =>
            {
                var result = await sessions.RefreshAsync(request.RefreshToken ?? "", ct);
                if (result.Status != RefreshStatus.Refreshed)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "InvalidRefreshToken");
                }

                var access = issuer.Issue(result.EmployeeId!.Value);
                return Results.Ok(new TokenResponse(access.Token, access.ExpiresAt, result.NewToken!));
            })
            .AllowAnonymous();

        // Ends all sessions of the signed-in person (refresh tokens); access tokens run out after their short lifetime.
        auth.MapPost("/logout", async (ClaimsPrincipal user, Access.Core.Login.IRefreshTokenStore tokens, CancellationToken ct) =>
            {
                if (!Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var employeeId))
                {
                    return Results.Forbid();
                }

                await tokens.RevokeAllAsync(employeeId, ct);
                return Results.NoContent();
            })
            .RequireAuthorization();

        MapStepUp(auth);

        // Public key and discovery: other services configure Authentication:Authority with the address of Access.
        app.MapGet("/.well-known/openid-configuration", (HttpContext http, AccessTokenIssuer issuer) =>
            {
                var root = $"{http.Request.Scheme}://{http.Request.Host}";
                return Results.Ok(new
                {
                    issuer = issuer.Issuer,
                    jwks_uri = $"{root}/.well-known/jwks.json",
                    id_token_signing_alg_values_supported = Rs256,
                    subject_types_supported = Public,
                    response_types_supported = Token,
                });
            })
            .AllowAnonymous();
        app.MapGet("/.well-known/jwks.json", (AccessTokenIssuer issuer) => Results.Ok(issuer.Jwks())).AllowAnonymous();
        return app;
    }

    /// <summary>
    /// Second factor with Microsoft Entra: <c>start</c> returns the address for the browser, <c>callback</c> is where Entra sends the browser back
    /// (anonymous, redirects to the portal with <c>?result=ok</c> or <c>?result=failed</c> and nothing more), <c>token</c> exchanges a valid confirmation
    /// for an access token with the claim <c>mfa_until</c>. Details of failures are only logged as codes.
    /// </summary>
    private static void MapStepUp(RouteGroupBuilder auth)
    {
        auth.MapPost("/step-up/start", async (ClaimsPrincipal user, StepUpService stepUp, CancellationToken ct) =>
            {
                if (Actor(user) is not { } employeeId)
                {
                    return Results.Forbid();
                }

                var started = await stepUp.StartAsync(employeeId, ct);
                return started.Status switch
                {
                    StartStatus.Started => Results.Ok(new StepUpStartResponse(started.AuthorizationUrl!)),
                    StartStatus.NoLink => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "NoEntraLink"),
                    _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "StepUpUnavailable"),
                };
            })
            .RequireAuthorization();

        auth.MapGet("/step-up/callback", async (string? code, string? state, string? error, StepUpService stepUp, StepUpOptions options, CancellationToken ct) =>
            {
                if (!options.Enabled)
                {
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "StepUpUnavailable");
                }

                var result = await stepUp.CompleteAsync(code, state, !string.IsNullOrEmpty(error), ct);
                return Results.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(options.ReturnUrl!, "result", result.Succeeded ? "ok" : "failed"));
            })
            .AllowAnonymous();

        auth.MapPost("/step-up/token", async (ClaimsPrincipal user, StepUpService stepUp, AccessTokenIssuer issuer, CancellationToken ct) =>
            {
                if (Actor(user) is not { } employeeId)
                {
                    return Results.Forbid();
                }

                if (await stepUp.ConfirmedUntilAsync(employeeId, ct) is not { } until)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "SecondFactorRequired");
                }

                var access = issuer.Issue(employeeId, until);
                return Results.Ok(new StepUpTokenResponse(access.Token, access.ExpiresAt));
            })
            .RequireAuthorization();
    }

    private static Guid? Actor(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : null;

    private static async Task<TokenResponse> Tokens(Guid employeeId, SessionService sessions, AccessTokenIssuer issuer, CancellationToken ct)
    {
        var access = issuer.Issue(employeeId);
        return new TokenResponse(access.Token, access.ExpiresAt, await sessions.StartAsync(employeeId, ct));
    }

    /// <summary>
    /// Caller for the per-client limit. TODO(Betrieb): behind a proxy (App Service) forwarded headers must be configured with
    /// the known proxies, otherwise all callers share the proxy's address.
    /// </summary>
    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
