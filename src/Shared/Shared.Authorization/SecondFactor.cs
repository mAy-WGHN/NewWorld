using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Shared.Authorization;

/// <summary>
/// The person has confirmed a second factor that has not run out yet: the token carries <c>mfa_until</c> (Unix seconds, set by the step-up of
/// Access, never by a normal login or refresh) and that moment lies in the future (<see cref="TimeProvider"/>). Fail closed: no claim, no access.
/// </summary>
public sealed class SecondFactorRequirement : IAuthorizationRequirement;

/// <summary>
/// Succeeds only; it never fails explicitly, so that the authorization result still lists every unmet requirement and
/// <see cref="SecondFactorResultHandler"/> can tell "only the second factor is missing" from "privilege missing".
/// </summary>
public sealed class SecondFactorHandler(TimeProvider time) : AuthorizationHandler<SecondFactorRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SecondFactorRequirement requirement)
    {
        if (context.User.FindFirst(AppClaimTypes.SecondFactorUntil)?.Value is { } value
            && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var until)
            && time.GetUtcNow().ToUnixTimeSeconds() < until)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class SecondFactorAuthorization
{
    /// <summary>Problem title of the 403 that tells a frontend to start the step-up.</summary>
    public const string ProblemTitle = "SecondFactorRequired";

    /// <summary>
    /// The check failed only because the second factor is missing or expired (the privilege itself is held). For endpoint handlers that call
    /// <see cref="IAuthorizationService"/> themselves and want to answer like the middleware does.
    /// </summary>
    public static bool OnlySecondFactorMissing(this AuthorizationResult result) =>
        !result.Succeeded
        && result.Failure is { FailedRequirements: { } failed }
        && failed.Any()
        && failed.All(r => r is SecondFactorRequirement);
}

/// <summary>
/// Answers 403 with the problem title <c>SecondFactorRequired</c> when only the second factor is missing; everything else is left to the
/// default handling (401 / plain 403), so a missing privilege reveals nothing.
/// </summary>
public sealed class SecondFactorResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure is { FailedRequirements: { } failed }
            && failed.Any() && failed.All(r => r is SecondFactorRequirement))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: SecondFactorAuthorization.ProblemTitle).ExecuteAsync(context);
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
