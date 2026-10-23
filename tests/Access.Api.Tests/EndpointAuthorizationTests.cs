using System.Net;
using Access.Api;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>Negative authorization per endpoint: anonymous → 401, without the privilege or role → 403.</summary>
public sealed class EndpointAuthorizationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private AccessApi _api = null!;

    public async Task InitializeAsync() => _api = await StartAsync(postgres);

    public async Task DisposeAsync() => await _api.DisposeAsync();

    public static TheoryData<string, string> UserEndpoints => new()
    {
        { "GET", "/api/catalog" },
        { "GET", "/api/roles" },
        { "POST", "/api/roles" },
        { "PUT", $"/api/roles/{Guid.Empty}" },
        { "DELETE", $"/api/roles/{Guid.Empty}" },
        { "GET", "/api/scopes" },
        { "POST", "/api/scopes" },
        { "PUT", $"/api/scopes/{Guid.Empty}" },
        { "DELETE", $"/api/scopes/{Guid.Empty}" },
        { "GET", $"/api/people/{Max}/assignments" },
        { "POST", $"/api/people/{Max}/assignments" },
        { "DELETE", $"/api/assignments/{Guid.Empty}" },
        { "GET", $"/api/people/{Max}/responsibilities" },
        { "POST", "/api/responsibilities" },
        { "DELETE", $"/api/responsibilities/{Guid.Empty}" },
        { "POST", "/api/custom-privileges" },
        { "DELETE", "/api/custom-privileges/x:y" },
    };

    public static TheoryData<string, string, string> ServiceEndpoints => new()
    {
        { "PUT", "/internal/apps/workforce/catalog", AccessRoles.CatalogRegister },
        { "GET", "/internal/permissions", AccessRoles.PermissionsRead },
        { "GET", "/internal/responsibilities", AccessRoles.PermissionsRead },
    };

    private static object? Body(string method) => method is "POST" or "PUT" ? new { } : null;

    [Theory]
    [MemberData(nameof(UserEndpoints))]
    public async Task Person_without_the_privilege_is_forbidden(string method, string url) =>
        (await _api.SendAsync(new HttpMethod(method), url, r => r.AsUser(Erika), Body(method))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Theory]
    [MemberData(nameof(UserEndpoints))]
    public async Task Anonymous_is_unauthorized(string method, string url) =>
        (await _api.SendAsync(new HttpMethod(method), url, null, Body(method))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [MemberData(nameof(ServiceEndpoints))]
    public async Task Internal_endpoints_refuse_people_and_services_without_the_role(string method, string url, string role)
    {
        (await _api.SendAsync(new HttpMethod(method), url, r => r.AsUser(Admin), Body(method))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.SendAsync(new HttpMethod(method), url, r => r.AsService("Other.Service.Role"), Body(method))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        role.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Me_privileges_needs_a_signed_in_person()
    {
        (await _api.SendAsync(HttpMethod.Get, "/api/me/privileges", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _api.SendAsync(HttpMethod.Get, "/api/me/privileges", r => r.AsService(AccessRoles.PermissionsRead))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _api.SendAsync(HttpMethod.Get, "/api/me/privileges", r => r.AsUser(Erika))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
