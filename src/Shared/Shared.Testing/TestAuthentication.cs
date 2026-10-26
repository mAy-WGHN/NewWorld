using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Shared.Testing;

/// <summary>A privilege for <see cref="TestAuthentication.Grant"/>; restricted to the given stores/employees unless unrestricted.</summary>
public sealed record TestGrant(string Key, IEnumerable<Guid>? StoreIds = null, IEnumerable<Guid>? EmployeeIds = null, bool Unrestricted = false)
{
    public static TestGrant Everywhere(string key) => new(key, Unrestricted: true);
}

/// <summary>Helpers around <see cref="TestAuthHandler"/>: register it, act as a person or service, grant privileges.</summary>
public static class TestAuthentication
{
    /// <summary>Registers the test handler as the default scheme.</summary>
    public static AuthenticationBuilder AddTestAuthentication(this IServiceCollection services) =>
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

    /// <summary>Sends the request as the given person.</summary>
    public static HttpRequestMessage AsUser(this HttpRequestMessage request, Guid employeeId)
    {
        request.Headers.Add(TestAuthHandler.EmployeeHeader, employeeId.ToString());
        return request;
    }

    /// <summary>Sends the request as a service with the given service roles.</summary>
    public static HttpRequestMessage AsService(this HttpRequestMessage request, params string[] roles)
    {
        request.Headers.Add(TestAuthHandler.ServiceRolesHeader, string.Join(',', roles));
        return request;
    }

    /// <summary>Sets the privileges of a person in the permission cache (next version), as if <c>PermissionsChanged</c> had arrived.</summary>
    public static PermissionSnapshot Grant(this IPermissionCache cache, Guid employeeId, params TestGrant[] grants)
    {
        var version = cache.TryGet(employeeId, out var current) ? current!.Version + 1 : 1;
        var privileges = grants.ToDictionary(
            g => g.Key,
            g => g.Unrestricted ? PrivilegeReach.Everywhere : PrivilegeReach.Stores(g.StoreIds ?? [], g.EmployeeIds ?? []));
        var snapshot = new PermissionSnapshot(employeeId, version, privileges);
        cache.Apply(snapshot);
        return snapshot;
    }
}
