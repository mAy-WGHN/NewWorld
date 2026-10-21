using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Testing;

namespace Shared.Authorization.Tests;

public sealed class ResourceAuthorizationTests : IAsyncLifetime
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Store1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Store2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private readonly InMemoryPermissionCache _cache = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // Anna: manages store 1, may access herself and Ben. Ben: only himself, no store.
        _cache.Apply(new PermissionSnapshot(Anna, 1, "oid-anna", new HashSet<Guid> { Store1 }, new HashSet<Guid> { Anna, Ben }));
        _cache.Apply(new PermissionSnapshot(Ben, 1, "oid-ben", new HashSet<Guid>(), new HashSet<Guid> { Ben }));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPermissionCache>(_cache);
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName).AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddSharedAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/stores/{storeId:guid}", async (Guid storeId, IAuthorizationService auth, HttpContext ctx) =>
            (await auth.AuthorizeAsync(ctx.User, new StoreResource(storeId), AppPolicies.CanAccessStore)).Succeeded
                ? Results.Ok() : Results.Forbid()).RequireAuthorization();
        _app.MapGet("/employees/{employeeId:guid}", async (Guid employeeId, IAuthorizationService auth, HttpContext ctx) =>
            (await auth.AuthorizeAsync(ctx.User, new EmployeeResource(employeeId), AppPolicies.CanAccessEmployee)).Succeeded
                ? Results.Ok() : Results.Forbid()).RequireAuthorization();
        _app.MapGet("/hr-only", () => "ok").RequireAuthorization(AppPolicies.RequireHr);
        _app.MapGet("/managers", () => "ok").RequireAuthorization(AppPolicies.RequireStoreManager);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<HttpStatusCode> Get(string url, string? oid, params string[] roles)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (oid is not null)
        {
            request.Headers.Add(TestAuthHandler.ObjectIdHeader, oid);
        }

        if (roles.Length > 0)
        {
            request.Headers.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Anonymous_is_unauthorized() =>
        (await Get($"/stores/{Store1}", oid: null)).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task User_may_access_a_store_in_scope() =>
        (await Get($"/stores/{Store1}", "oid-anna", "StoreManager")).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task User_may_not_access_a_store_outside_scope() =>
        (await Get($"/stores/{Store2}", "oid-anna", "StoreManager")).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task User_without_any_store_scope_is_denied() =>
        (await Get($"/stores/{Store1}", "oid-ben", "Employee")).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Unknown_identity_is_denied_fail_closed() =>
        (await Get($"/stores/{Store1}", "oid-ghost", "StoreManager")).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task User_may_access_themselves() =>
        (await Get($"/employees/{Ben}", "oid-ben", "Employee")).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task User_may_access_an_employee_in_scope() =>
        (await Get($"/employees/{Ben}", "oid-anna", "StoreManager")).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task User_may_not_access_an_employee_outside_scope() =>
        (await Get($"/employees/{Anna}", "oid-ben", "Employee")).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Role_policy_allows_the_role() =>
        (await Get("/hr-only", "oid-anna", "Hr")).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Role_policy_denies_other_roles() =>
        (await Get("/hr-only", "oid-anna", "StoreManager")).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Executive_satisfies_the_store_manager_policy() =>
        (await Get("/managers", "oid-anna", "Executive")).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Employee_does_not_satisfy_the_store_manager_policy() =>
        (await Get("/managers", "oid-ben", "Employee")).ShouldBe(HttpStatusCode.Forbidden);
}
