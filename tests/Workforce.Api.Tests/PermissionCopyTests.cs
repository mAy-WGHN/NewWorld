using System.Net;
using Access.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using Workforce.Api;
using Workforce.Infrastructure;

namespace Workforce.Api.Tests;

/// <summary>
/// Plan, phase 4: a consumer API with Shared.Authorization receives PermissionsChanged and afterwards denies access to a withdrawn
/// store, checked locally without asking Access, also after a restart (copy restored from its own database).
/// </summary>
public sealed class PermissionCopyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid F017 = Guid.Parse("00000000-0000-0000-0000-000000000017");
    private static readonly Guid F018 = Guid.Parse("00000000-0000-0000-0000-000000000018");

    /// <summary>The real Workforce API plus a probe endpoint that checks like a future business endpoint would.</summary>
    private sealed class WorkforceApi(string connectionString) : WebApplicationFactory<ApiMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddTestAuthentication();
                services.AddSingleton<IStartupFilter, ProbeEndpoint>();
            });
        }
    }

    private sealed class ProbeEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapGet("/probe/stores/{storeId:guid}/absences", async (Guid storeId, IAuthorizationService auth, HttpContext context) =>
                (await auth.AuthorizeAsync(context.User, new StoreResource(storeId), AppPolicies.Privilege("absence:read-availability"))).Succeeded
                    ? Results.Ok()
                    : Results.Forbid()).RequireAuthorization());
        };
    }

    private static PermissionsChanged Permissions(long version, params Guid[] stores) => new(
        Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero), Max, version, [],
        stores, [Max], ["Filialleitung"],
        stores.Length == 0 ? [] : [new PrivilegeGrant("absence:read-availability", false, stores, [Max])]);

    private static async Task Deliver(WorkforceApi api, PermissionsChanged message)
    {
        var result = await api.Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(message.MessageId.ToString(), nameof(PermissionsChanged), MessageJson.Serialize(message), 1), CancellationToken.None);
        result.Outcome.ShouldBe(DispatchOutcome.Completed);
    }

    private static async Task<HttpStatusCode> Probe(WorkforceApi api, Guid store)
    {
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/probe/stores/{store}/absences").AsUser(Max);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Withdrawn_store_is_denied_after_permissions_changed_and_after_a_restart()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var api = new WorkforceApi(connectionString))
        {
            (await Probe(api, F017)).ShouldBe(HttpStatusCode.Forbidden); // no copy yet: fail closed

            await Deliver(api, Permissions(1, F017, F018));
            (await Probe(api, F017)).ShouldBe(HttpStatusCode.OK);
            (await Probe(api, F018)).ShouldBe(HttpStatusCode.OK);

            await Deliver(api, Permissions(2, F018)); // F017 withdrawn
            (await Probe(api, F017)).ShouldBe(HttpStatusCode.Forbidden);
            (await Probe(api, F018)).ShouldBe(HttpStatusCode.OK);

            await Deliver(api, Permissions(1, F017, F018)); // late, older message: ignored
            (await Probe(api, F017)).ShouldBe(HttpStatusCode.Forbidden);
        }

        await using (var restarted = new WorkforceApi(connectionString))
        {
            (await Probe(restarted, F017)).ShouldBe(HttpStatusCode.Forbidden);
            (await Probe(restarted, F018)).ShouldBe(HttpStatusCode.OK);
        }
    }
}
