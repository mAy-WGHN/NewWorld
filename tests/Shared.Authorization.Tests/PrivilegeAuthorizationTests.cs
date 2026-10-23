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

/// <summary>Local checks "privilege + store/employee" against the permission copy, end to end through ASP.NET Core.</summary>
public sealed class PrivilegeAuthorizationTests : IAsyncLifetime
{
    private const string ErasureRole = "BcIntegration.EmployeeErasure.Request";
    private static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-00000000000a");   // store manager of store 1
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-00000000000b"); // employee of store 1
    private static readonly Guid Petra = Guid.Parse("00000000-0000-0000-0000-00000000000c"); // HR, unrestricted
    private static readonly Guid Store1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Store2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private readonly InMemoryPermissionCache _cache = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _cache.Grant(Max, new TestGrant("absence:read", StoreIds: [Store1], EmployeeIds: [Erika]), new TestGrant("absence:approve", StoreIds: [Store1], EmployeeIds: [Erika]));
        _cache.Grant(Erika, new TestGrant("absence:read-own"));
        _cache.Grant(Petra, TestGrant.Everywhere("absence:read"));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPermissionCache>(_cache);
        builder.Services.AddTestAuthentication();
        builder.Services.AddSharedAuthorization(o => o.ServiceRoles.Add(ErasureRole));
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/stores/{storeId:guid}/absences", async (Guid storeId, IAuthorizationService auth, HttpContext ctx) =>
            (await auth.AuthorizeAsync(ctx.User, new StoreResource(storeId), AppPolicies.Privilege("absence:read"))).Succeeded
                ? Results.Ok() : Results.Forbid()).RequireAuthorization();
        _app.MapPost("/employees/{employeeId:guid}/absences/approve", async (Guid employeeId, IAuthorizationService auth, HttpContext ctx) =>
            (await auth.AuthorizeAsync(ctx.User, new EmployeeResource(employeeId), AppPolicies.Privilege("absence:approve"))).Succeeded
                ? Results.Ok() : Results.Forbid()).RequireAuthorization();
        _app.MapGet("/me/absences", () => "ok").RequireAuthorization(AppPolicies.Privilege("absence:read-own"));
        _app.MapPost("/internal/erasure", () => "ok").RequireAuthorization(AppPolicies.ServiceRole(ErasureRole));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<HttpStatusCode> Send(HttpMethod method, string url, Func<HttpRequestMessage, HttpRequestMessage>? identity)
    {
        using var request = new HttpRequestMessage(method, url);
        identity?.Invoke(request);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Anonymous_is_unauthorized() =>
        (await Send(HttpMethod.Get, $"/stores/{Store1}/absences", null)).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Privilege_in_reach_of_the_store_is_allowed() =>
        (await Send(HttpMethod.Get, $"/stores/{Store1}/absences", r => r.AsUser(Max))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Privilege_outside_the_reach_is_forbidden() =>
        (await Send(HttpMethod.Get, $"/stores/{Store2}/absences", r => r.AsUser(Max))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Missing_privilege_is_forbidden_even_in_the_own_store() =>
        (await Send(HttpMethod.Get, $"/stores/{Store1}/absences", r => r.AsUser(Erika))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Unrestricted_privilege_applies_to_every_store() =>
        (await Send(HttpMethod.Get, $"/stores/{Store2}/absences", r => r.AsUser(Petra))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Employee_in_reach_is_allowed() =>
        (await Send(HttpMethod.Post, $"/employees/{Erika}/absences/approve", r => r.AsUser(Max))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Own_person_is_not_implicitly_in_reach() =>
        (await Send(HttpMethod.Post, $"/employees/{Max}/absences/approve", r => r.AsUser(Max))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Privilege_without_resource_requires_having_it_at_all()
    {
        (await Send(HttpMethod.Get, "/me/absences", r => r.AsUser(Erika))).ShouldBe(HttpStatusCode.OK);
        (await Send(HttpMethod.Get, "/me/absences", r => r.AsUser(Max))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Person_without_any_permission_copy_is_denied_fail_closed() =>
        (await Send(HttpMethod.Get, "/me/absences", r => r.AsUser(Guid.NewGuid()))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Service_role_allows_the_service_and_denies_people()
    {
        (await Send(HttpMethod.Post, "/internal/erasure", r => r.AsService(ErasureRole))).ShouldBe(HttpStatusCode.OK);
        (await Send(HttpMethod.Post, "/internal/erasure", r => r.AsUser(Petra))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Service_cannot_act_as_a_person() =>
        (await Send(HttpMethod.Get, "/me/absences", r => r.AsService(ErasureRole))).ShouldBe(HttpStatusCode.Forbidden);
}
