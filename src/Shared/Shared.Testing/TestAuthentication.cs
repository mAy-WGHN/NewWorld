using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Shared.Testing;

/// <summary>Helpers around <see cref="TestAuthHandler"/>: register it, act as a user, grant scopes.</summary>
public static class TestAuthentication
{
    /// <summary>Registers the test handler as the default scheme (replaces Entra in tests).</summary>
    public static AuthenticationBuilder AddTestAuthentication(this IServiceCollection services) =>
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

    /// <summary>Sends the request as the given identity (provider object ID) with the given app roles.</summary>
    public static HttpRequestMessage AsUser(this HttpRequestMessage request, string objectId, params string[] roles)
    {
        request.Headers.Add(TestAuthHandler.ObjectIdHeader, objectId);
        if (roles.Length > 0)
        {
            request.Headers.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return request;
    }

    /// <summary>
    /// Sets the scopes of a person in the permission cache (next version), as if <c>PermissionsChanged</c> had arrived.
    /// The person may always access themself.
    /// </summary>
    public static PermissionSnapshot Grant(
        this IPermissionCache cache,
        Guid employeeId,
        string objectId,
        IEnumerable<Guid>? storeIds = null,
        IEnumerable<Guid>? employeeIds = null)
    {
        var version = cache.TryGet(employeeId, out var current) ? current!.Version + 1 : 1;
        var employees = new HashSet<Guid>(employeeIds ?? []) { employeeId };
        var snapshot = new PermissionSnapshot(employeeId, version, objectId, new HashSet<Guid>(storeIds ?? []), employees);
        cache.Apply(snapshot);
        return snapshot;
    }
}
