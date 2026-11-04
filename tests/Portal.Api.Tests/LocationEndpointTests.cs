using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Portal.Infrastructure;
using Portal.Infrastructure.Messaging;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The list of stores for the report form, as the existing frontend reads it (<c>GET /api/locations</c>, envelope <c>{success,data:{items,meta}}</c>).
/// Public like the report form itself; only open stores, because a report for a closed store is refused.
/// </summary>
public sealed class LocationEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<System.Text.Json.JsonElement> ListAsync(PortalApi api, Guid? person = null)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/locations", person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static List<string> Codes(System.Text.Json.JsonElement data) =>
        [.. data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("storeCode").GetString()!)];

    [Fact]
    public async Task The_open_stores_are_listed_without_signing_in_by_store_number()
    {
        await using var api = await StartAsync(postgres);

        var data = await ListAsync(api);

        Codes(data).ShouldBe(["017", "018", "019"]);
        var first = data.GetProperty("items")[0];
        first.GetProperty("id").GetGuid().ShouldBe(StoreA);
        first.GetProperty("name").GetString().ShouldBe("Store A");
        first.GetProperty("archived").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_answer_has_the_paging_meta_the_existing_frontend_reads()
    {
        await using var api = await StartAsync(postgres);

        var meta = (await ListAsync(api)).GetProperty("meta");

        meta.GetProperty("total").GetInt32().ShouldBe(3);
        meta.GetProperty("offset").GetInt32().ShouldBe(0);
        meta.GetProperty("limit").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task A_closed_store_is_not_offered()
    {
        await using var api = await StartAsync(postgres);

        Codes(await ListAsync(api)).ShouldNotContain("020");
    }

    [Fact]
    public async Task A_new_store_appears_in_its_place_and_a_closed_one_disappears()
    {
        await using var api = await StartAsync(postgres);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Stores.Add(new StoreRecord { StoreId = Guid.NewGuid(), StoreNumber = "009", Name = "Store Neu", IsActive = true });
            db.Stores.Single(s => s.StoreNumber == "018").IsActive = false;
            await db.SaveChangesAsync();
        }

        Codes(await ListAsync(api)).ShouldBe(["009", "017", "019"]);
    }

    [Fact]
    public async Task A_store_of_the_list_is_accepted_for_a_report()
    {
        await using var api = await StartAsync(postgres);
        var id = (await ListAsync(api)).GetProperty("items")[1].GetProperty("id").GetGuid();

        var report = await api.ReportAsync(new { personnelNumber = 4711, category = "bug", severity = "medium", description = "Scanner defekt", locationIds = new[] { id } });

        report.GetProperty("locations")[0].GetProperty("id").GetGuid().ShouldBe(id);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task The_list_cannot_be_changed_here_not_even_by_the_administration(string method)
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(new HttpMethod(method), "/api/locations", Hanna, new { name = "X", storeCode = "999" });

        ((int)response.StatusCode).ShouldBeOneOf(404, 405);
    }
}
