using System.Net;
using BcIntegration.Api;
using BcIntegration.Core.MasterData;
using BcIntegration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Testing;

namespace BcIntegration.Api.Tests;

public sealed class ErasureEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<(BcIntegrationApiFactory Api, HttpClient Client, Guid EmployeeId)> StartAsync()
    {
        var api = new BcIntegrationApiFactory(await postgres.CreateDatabaseAsync());
        var client = api.CreateClient();
        var employeeId = Guid.NewGuid();
        await using var scope = api.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMasterDataStore>();
        await store.SaveEmployeeAsync(employeeId, new EmployeeState("10042", "Erika Mustermann", null, null, null, null, null, null), CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);
        return (api, client, employeeId);
    }

    private static async Task<HttpStatusCode> Erase(HttpClient client, Guid employeeId, Func<HttpRequestMessage, HttpRequestMessage>? identity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/employees/{employeeId}/erasure");
        identity?.Invoke(request);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Service_with_the_erasure_role_releases_the_erasure()
    {
        var (api, client, employeeId) = await StartAsync();
        await using var _ = api;

        (await Erase(client, employeeId, r => r.AsService(BcIntegrationRoles.EmployeeErasureRequest))).ShouldBe(HttpStatusCode.Accepted);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BcIntegrationDbContext>();
        (await db.Set<OutboxMessage>().SingleAsync()).MessageType.ShouldBe("EmployeeDataErasureRequested");
        (await db.EmployeeStates.CountAsync()).ShouldBe(0);
        (await db.ErasedEmployees.SingleAsync()).EmployeeId.ShouldBe(employeeId);
    }

    [Fact]
    public async Task Unknown_employee_is_not_found()
    {
        var (api, client, _) = await StartAsync();
        await using var __ = api;

        (await Erase(client, Guid.NewGuid(), r => r.AsService(BcIntegrationRoles.EmployeeErasureRequest))).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task People_are_forbidden_even_with_all_privileges()
    {
        var (api, client, employeeId) = await StartAsync();
        await using var _ = api;
        var ceo = Guid.NewGuid();
        api.Services.GetRequiredService<Shared.Authorization.IPermissionCache>().Grant(ceo, TestGrant.Everywhere("access:assignment:create"));

        (await Erase(client, employeeId, r => r.AsUser(ceo))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthorized()
    {
        var (api, client, employeeId) = await StartAsync();
        await using var _ = api;

        (await Erase(client, employeeId, identity: null)).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
