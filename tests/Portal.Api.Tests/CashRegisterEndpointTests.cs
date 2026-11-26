using System.Net;
using System.Text.Json;
using Portal.Core;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The Kassenregister (Filialbetrieb): cash registers per store with seal status. Recording or editing a register removes its seal, renewing sets it
/// (TODO(Fachlogik) in <c>CashRegisterRecord</c>). Rights are scopable by store; the history of a store keeps deleted registers by name.
/// </summary>
public sealed class CashRegisterEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<JsonElement> CreateAsync(PortalApi api, Guid person, Guid storeId, string name)
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", person, new { storeId, name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task SealAsync(PortalApi api, Guid id)
    {
        using var response = await CashRegisterTestHelpers.SealAsync(api, id, Hanna);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ListAsync(PortalApi api, Guid person, string query = "")
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/cash-registers" + query, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    [Fact]
    public async Task A_recorded_register_is_listed_not_sealed_with_the_initials_of_who_recorded_it()
    {
        await using var api = await StartAsync(postgres);

        var created = await CreateAsync(api, Hanna, StoreA, "Kasse 1");

        created.GetProperty("sealed").GetBoolean().ShouldBeFalse();
        created.GetProperty("recordedBy").GetString().ShouldBe("HA");
        var data = await ListAsync(api, Hanna);
        var item = data.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("name").GetString().ShouldBe("Kasse 1");
        item.GetProperty("storeNumber").GetString().ShouldBe("017");
        item.GetProperty("storeName").GetString().ShouldBe("Store A");
        item.GetProperty("lastRecordedAt").GetDateTimeOffset().ShouldBe(Start);
    }

    [Fact]
    public async Task The_summary_counts_the_open_stores_in_reach_and_the_registers_by_status()
    {
        await using var api = await StartAsync(postgres);
        await CreateAsync(api, Hanna, StoreA, "Kasse 1");
        var second = await CreateAsync(api, Hanna, StoreB, "Kasse 1");
        using var seal = await CashRegisterTestHelpers.SealAsync(api, second.GetProperty("id").GetGuid(), Hanna);
        seal.StatusCode.ShouldBe(HttpStatusCode.OK);

        var summary = (await ListAsync(api, Hanna)).GetProperty("summary");

        summary.GetProperty("stores").GetInt32().ShouldBe(3); // A, B, C (D is closed)
        summary.GetProperty("registers").GetInt32().ShouldBe(2);
        summary.GetProperty("sealed").GetInt32().ShouldBe(1);
        summary.GetProperty("notSealed").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Renewing_the_seal_seals_and_editing_without_a_seal_state_keeps_it()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();

        using var seal = await CashRegisterTestHelpers.SealAsync(api, id, Hanna);
        (await DataAsync(seal)).GetProperty("sealed").GetBoolean().ShouldBeTrue();

        using var edit = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Hanna, new { storeId = StoreA, name = "Kasse eins" });
        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        var edited = await DataAsync(edit);
        edited.GetProperty("sealed").GetBoolean().ShouldBeTrue();
        edited.GetProperty("name").GetString().ShouldBe("Kasse eins");
    }

    [Fact]
    public async Task The_seal_can_be_lifted_when_editing_and_set_again()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();
        await SealAsync(api, id);

        using var lift = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Hanna, new { storeId = StoreA, name = "Kasse 1", @sealed = false });
        var lifted = await DataAsync(lift);
        lifted.GetProperty("sealed").GetBoolean().ShouldBeFalse();
        lifted.GetProperty("stateChangedAt").GetDateTimeOffset().ShouldBe(Start); // the state is a plain flag, remembered with when it was set

        using var again = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Hanna, await CashRegisterTestHelpers.SealedBodyAsync(api, Hanna, StoreA, "Kasse 1"));
        (await DataAsync(again)).GetProperty("sealed").GetBoolean().ShouldBeTrue();

        using var history = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreA}", Hanna);
        (await DataAsync(history)).EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ShouldBe(["sealed", "unsealed", "sealed", "recorded"]);
    }

    [Fact]
    public async Task Lifting_the_seal_has_its_own_action_and_does_nothing_if_there_is_no_seal()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();
        await SealAsync(api, id);

        using var unseal = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/unseal", Hanna);
        unseal.StatusCode.ShouldBe(HttpStatusCode.OK, await unseal.Content.ReadAsStringAsync());
        (await DataAsync(unseal)).GetProperty("sealed").GetBoolean().ShouldBeFalse();
        using var twice = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/unseal", Hanna);
        twice.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var history = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreA}", Hanna);
        (await DataAsync(history)).EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ShouldBe(["unsealed", "sealed", "recorded"]);
    }

    [Fact]
    public async Task The_state_is_a_flag_that_moves_only_by_the_actions_and_a_renewed_seal_stays_sealed()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();

        async Task<bool> IsSealedAsync() =>
            (await ListAsync(api, Hanna)).GetProperty("items").EnumerateArray().Single().GetProperty("sealed").GetBoolean();

        (await IsSealedAsync()).ShouldBeFalse();
        await SealAsync(api, id);
        await SealAsync(api, id); // renewing a seal that is there stays sealed
        (await IsSealedAsync()).ShouldBeTrue();
        using (var rename = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Hanna, new { storeId = StoreA, name = "Kasse eins" }))
        {
            rename.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await IsSealedAsync()).ShouldBeTrue(); // editing without a state does not touch it
        (await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/unseal", Hanna)).Dispose();
        (await IsSealedAsync()).ShouldBeFalse();

        using var history = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreA}", Hanna);
        (await DataAsync(history)).EnumerateArray().Select(e => e.GetProperty("kind").GetString())
            .ShouldBe(["unsealed", "changed", "sealed", "sealed", "recorded"]);
    }

    [Fact]
    public async Task A_register_can_be_recorded_as_sealed_right_away()
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, await CashRegisterTestHelpers.SealedBodyAsync(api, Hanna, StoreA, "Kasse 1"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await DataAsync(response)).GetProperty("sealed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Lifting_the_seal_needs_the_right_to_record_and_a_register_in_reach()
    {
        await using var api = await StartAsync(postgres);
        var own = (await CreateAsync(api, Hanna, StoreA, "Kasse A")).GetProperty("id").GetGuid();
        var foreign = (await CreateAsync(api, Hanna, StoreB, "Kasse B")).GetProperty("id").GetGuid();
        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterRead, Unrestricted: true), new TestGrant(PortalPrivileges.CashRegisterRecord, StoreIds: [StoreA]));
        api.Grant(Ben, new TestGrant(PortalPrivileges.CashRegisterRead, Unrestricted: true));

        using var readOnly = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{own}/unseal", Ben);
        using var outOfReach = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{foreign}/unseal", Erika);
        using var unknown = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{Guid.NewGuid()}/unseal", Hanna);
        using var anonymous = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{own}/unseal", null);
        using var fine = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{own}/unseal", Erika);

        readOnly.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        outOfReach.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        fine.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_register_can_move_to_another_store_when_both_are_in_reach()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();

        using var edit = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Hanna, new { storeId = StoreB, name = "Kasse 1" });

        (await DataAsync(edit)).GetProperty("storeId").GetGuid().ShouldBe(StoreB);
    }

    [Fact]
    public async Task The_name_is_unique_within_a_store_but_not_across_stores()
    {
        await using var api = await StartAsync(postgres);
        await CreateAsync(api, Hanna, StoreA, "Kasse 1");

        using var duplicate = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, new { storeId = StoreA, name = "kasse 1" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await CreateAsync(api, Hanna, StoreB, "Kasse 1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_register_needs_a_name(string name)
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, new { storeId = StoreA, name });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("name");
    }

    [Fact]
    public async Task A_closed_or_unknown_store_is_refused()
    {
        await using var api = await StartAsync(postgres);

        using var closed = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, new { storeId = StoreD, name = "Kasse 1" });
        using var unknown = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, new { storeId = Guid.NewGuid(), name = "Kasse 1" });

        closed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_removes_the_register_and_the_history_keeps_it_by_name()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();

        using var delete = await api.SendAsync(HttpMethod.Delete, $"/api/cash-registers/{id}", Hanna);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(api, Hanna)).GetProperty("items").GetArrayLength().ShouldBe(0);
        using var history = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreA}", Hanna);
        var events = (await DataAsync(history)).EnumerateArray().ToList();
        events.Select(e => e.GetProperty("kind").GetString()).ShouldBe(["deleted", "recorded"]);
        events.ShouldAllBe(e => e.GetProperty("registerName").GetString() == "Kasse 1");
        events[0].GetProperty("by").GetString().ShouldBe("HA");
    }

    [Fact]
    public async Task The_history_lists_every_step_newest_first_and_can_be_limited_to_a_store()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();
        await CreateAsync(api, Hanna, StoreB, "Kasse 1");
        (await CashRegisterTestHelpers.SealAsync(api, id, Hanna)).Dispose();

        using var one = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreA}", Hanna);
        using var all = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/history", Hanna);

        (await DataAsync(one)).EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ShouldBe(["sealed", "recorded"]);
        (await DataAsync(all)).GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task Without_a_privilege_there_is_nothing_and_without_signing_in_there_is_401()
    {
        await using var api = await StartAsync(postgres);

        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/cash-registers", null);
        using var nobody = await api.SendAsync(HttpMethod.Get, "/api/cash-registers", Ben);
        using var create = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Ben, new { storeId = StoreA, name = "Kasse 1" });

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        nobody.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_alone_does_not_allow_changing_or_deleting()
    {
        await using var api = await StartAsync(postgres);
        var id = (await CreateAsync(api, Hanna, StoreA, "Kasse 1")).GetProperty("id").GetGuid();
        api.Grant(Erika, TestGrant.Everywhere(PortalPrivileges.CashRegisterRead));

        using var seal = await CashRegisterTestHelpers.SealAsync(api, id, Erika);
        using var edit = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Erika, new { storeId = StoreA, name = "X" });
        using var delete = await api.SendAsync(HttpMethod.Delete, $"/api/cash-registers/{id}", Erika);

        seal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_reach_limits_the_list_the_summary_and_the_history_to_the_stores_of_the_privilege()
    {
        await using var api = await StartAsync(postgres);
        await CreateAsync(api, Hanna, StoreA, "Kasse A");
        await CreateAsync(api, Hanna, StoreB, "Kasse B");
        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterRead, StoreIds: [StoreA]));

        var data = await ListAsync(api, Erika);

        data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ShouldBe(["Kasse A"]);
        data.GetProperty("summary").GetProperty("stores").GetInt32().ShouldBe(1);
        data.GetProperty("stores").GetArrayLength().ShouldBe(1);
        using var foreign = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreB}", Erika);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var own = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/history", Erika);
        (await DataAsync(own)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_register_of_a_store_out_of_reach_cannot_be_changed_and_a_move_there_is_refused()
    {
        await using var api = await StartAsync(postgres);
        var foreign = (await CreateAsync(api, Hanna, StoreB, "Kasse B")).GetProperty("id").GetGuid();
        var own = (await CreateAsync(api, Hanna, StoreA, "Kasse A")).GetProperty("id").GetGuid();
        api.Grant(Erika,
            new TestGrant(PortalPrivileges.CashRegisterRead, StoreIds: [StoreA]),
            new TestGrant(PortalPrivileges.CashRegisterRecord, StoreIds: [StoreA]),
            new TestGrant(PortalPrivileges.CashRegisterDelete, StoreIds: [StoreA]));

        using var seal = await CashRegisterTestHelpers.SealAsync(api, foreign, Erika);
        using var createThere = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Erika, new { storeId = StoreB, name = "Neu" });
        using var moveThere = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{own}", Erika, new { storeId = StoreB, name = "Kasse A" });
        using var deleteThere = await api.SendAsync(HttpMethod.Delete, $"/api/cash-registers/{foreign}", Erika);
        using var fine = await CashRegisterTestHelpers.SealAsync(api, own, Erika);

        seal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        createThere.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        moveThere.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deleteThere.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        fine.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_register_is_404()
    {
        await using var api = await StartAsync(postgres);

        using var seal = await CashRegisterTestHelpers.SealAsync(api, Guid.NewGuid(), Hanna);
        using var delete = await api.SendAsync(HttpMethod.Delete, $"/api/cash-registers/{Guid.NewGuid()}", Hanna);

        seal.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_store()
    {
        await using var api = await StartAsync(postgres);
        await CreateAsync(api, Hanna, StoreA, "Kasse A");
        await CreateAsync(api, Hanna, StoreB, "Kasse B");

        var data = await ListAsync(api, Hanna, $"?storeId={StoreB}");

        data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ShouldBe(["Kasse B"]);
        data.GetProperty("summary").GetProperty("registers").GetInt32().ShouldBe(2); // the summary is about everything in reach
    }
}
