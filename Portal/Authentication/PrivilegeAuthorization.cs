using Portal.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Portal.Authentication;

public static class AuthPolicies
{
    public const string PrivilegePrefix = "privilege:";
}

/// <summary>Verlangt ein Recht aus dem Katalog <see cref="Privileges"/>; fehlt es, antwortet die API mit 403.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HasPrivilegeAttribute(string privilege) : AuthorizeAttribute(AuthPolicies.PrivilegePrefix + privilege);

/// <summary>
/// Verlangt ein Recht der Ressource <typeparamref name="TEntity"/> (Entity mit <see cref="PrivilegeResourceAttribute"/>):
/// <c>[HasPrivilege&lt;Report&gt;(CrudAction.Read)]</c> für <c>report:read</c>, <c>[HasPrivilege&lt;Faq&gt;("assign")]</c> für eine Zusatzaktion.
/// Eine nicht freigegebene Aktion fällt beim Start auf (Exception), nicht erst im Betrieb.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HasPrivilegeAttribute<TEntity> : AuthorizeAttribute
{
    public HasPrivilegeAttribute(CrudAction action)
        : base(AuthPolicies.PrivilegePrefix + PrivilegeCatalog.PrivilegeOf(typeof(TEntity), action)) { }

    public HasPrivilegeAttribute(string action)
        : base(AuthPolicies.PrivilegePrefix + PrivilegeCatalog.PrivilegeOf(typeof(TEntity), action)) { }
}

public sealed record PrivilegeRequirement(string Privilege) : IAuthorizationRequirement;

/// <summary>Baut die Policy <c>privilege:&lt;recht&gt;</c> bei Bedarf, alle anderen Namen löst der Standard auf.</summary>
public sealed class PrivilegePolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(AuthPolicies.PrivilegePrefix, StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName);
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .AddRequirements(new PrivilegeRequirement(policyName[AuthPolicies.PrivilegePrefix.Length..])).Build();
    }
}

/// <summary>
/// Fragt Rollen-Rechte (mit dem Token der Anfrage), ob der User das Recht hat, und setzt dessen Reichweite für die Abfragen dieser Anfrage.
/// Lehnt Rollen-Rechte das Token ab (z. B. User deaktiviert), ist die Anfrage nicht angemeldet (401); ist es nicht erreichbar, lässt sich nichts klären (503, fail-closed).
/// </summary>
public sealed class PrivilegeHandler(IAccessDirectory directory, ScopeContext scopeContext, IHttpContextAccessor accessor) : AuthorizationHandler<PrivilegeRequirement>
{
    private const string Bearer = "Bearer ";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PrivilegeRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return; // Ungültiges/fehlendes Token: gar nicht erst bei Rollen-Rechte nachfragen.

        var http = accessor.HttpContext;
        var header = http?.Request.Headers.Authorization.ToString();
        if (http is null || header is null || !header.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase)) return;

        UserAccess? access;
        try
        {
            access = await directory.ResolveAsync(header[Bearer.Length..].Trim(), http.RequestAborted);
        }
        catch (AccessUnavailableException)
        {
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Die Rechteprüfung (Rollen-Rechte) ist nicht erreichbar.");
        }

        if (access is null) throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid JWT Token");

        // Der Scope zum verlangten Recht gilt für die Abfragen dieser Anfrage (z. B. Meldungen).
        if (access.Privileges.Contains(requirement.Privilege) && access.Scopes.TryGetValue(requirement.Privilege, out var scope))
        {
            scopeContext.Current = scope;
            context.Succeed(requirement);
        }
    }
}
