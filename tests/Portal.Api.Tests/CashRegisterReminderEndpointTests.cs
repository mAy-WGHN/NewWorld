using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Portal.Core;
using Portal.Infrastructure.CashRegisters;
using Shared.Messaging;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The daily reminder for cash registers that are not sealed and the insight of the district leaders. Store managers (the holders of
/// <c>cashregister:record</c> for exactly one store) are told for their store, the district leaders are <b>always</b> told (one message per district) and
/// <b>always</b> see every store of their district, whatever their privileges. The fixed clock is Friday 2026-10-23, 12:00 UTC = 14:00 local (CEST).
/// </summary>
public sealed class CashRegisterReminderEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid District = Guid.Parse("00000000-0000-0000-0000-0000000d1500");

    /// <summary>Hanna (administration) records a register; Erika manages store A, Ben leads the district with store B only (no privileges at all).</summary>
    private async Task<PortalApi> StartUpAsync(bool districtWithB = true)
    {
        var api = await StartAsync(postgres);
        var structure = new
        {
            districts = new object[] { new { id = District, name = "Süd", leaderIds = new[] { Ben } } },
            regions = Array.Empty<object>(),
            stores = districtWithB
                ? new object[] { new { storeId = StoreB, districtId = District, regionId = (Guid?)null } }
                : [],
        };
        using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", Hanna, structure);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterRecord, StoreIds: [StoreA]));
        return api;
    }

    private static async Task<Guid> RecordAsync(PortalApi api, Guid store, string name)
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, new { storeId = store, name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task SealAsync(PortalApi api, Guid id)
    {
        using var response = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static CashRegisterReminderBackgroundService Reminders(PortalApi api) =>
        api.Services.GetServices<IHostedService>().OfType<CashRegisterReminderBackgroundService>().Single();

    private static FakeTimeProvider Clock(PortalApi api) => api.Services.GetRequiredService<FakeTimeProvider>();

    private static async Task<List<SendNotification>> SentAsync(PortalApi api) =>
        [.. (await api.OutboxAsync()).Where(m => m.MessageType == nameof(SendNotification)).Select(m => MessageJson.Deserialize<SendNotification>(m.Payload)).Where(n => n.TemplateKey.StartsWith("portal.cash-register-", StringComparison.Ordinal))];

    private static async Task RunAtAsync(PortalApi api, DateTimeOffset utc)
    {
        Clock(api).SetUtcNow(utc);
        await Reminders(api).RunOnceAsync(CancellationToken.None);
    }

    private static readonly DateTimeOffset Evening = new(2026, 10, 23, 16, 0, 0, TimeSpan.Zero); // 18:00 local (CEST)

    // ------------------------------------------------------------------ the reminder

    [Fact]
    public async Task Nothing_goes_out_before_the_time()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreA, "Kasse 1");

        await RunAtAsync(api, new(2026, 10, 23, 15, 59, 0, TimeSpan.Zero)); // 17:59 local

        (await SentAsync(api)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_store_managers_are_told_for_their_store_and_the_district_leaders_always()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreA, "Kasse 1");
        await RecordAsync(api, StoreB, "Kasse 1");

        await RunAtAsync(api, Evening);

        var sent = await SentAsync(api);
        sent.Count.ShouldBe(2);
        var store = sent.Single(n => n.TemplateKey == CashRegisterNotifier.UnsealedStore);
        store.RecipientEmployeeIds.ShouldBe([Erika]);
        var district = sent.Single(n => n.TemplateKey == CashRegisterNotifier.UnsealedDistrict);
        district.RecipientEmployeeIds.ShouldBe([Ben]);
        sent.ShouldAllBe(n => n.Data.Count == 0 && n.DeepLink == "/cash-registers");
        store.Channels.ShouldBe([NotificationChannel.InApp, NotificationChannel.Push]);
        district.Channels.ShouldBe([NotificationChannel.InApp]); // the district leaders are informed quietly: an info in the portal, no push
    }

    [Fact]
    public async Task A_district_with_several_stores_gets_one_message_not_one_per_store()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreB, "Kasse 1");
        await RecordAsync(api, StoreB, "Kasse 2");

        await RunAtAsync(api, Evening);

        (await SentAsync(api)).ShouldHaveSingleItem().TemplateKey.ShouldBe(CashRegisterNotifier.UnsealedDistrict);
    }

    [Fact]
    public async Task A_day_is_reminded_once_and_the_next_day_again()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreB, "Kasse 1");

        await RunAtAsync(api, Evening);
        await RunAtAsync(api, Evening.AddHours(3));
        (await SentAsync(api)).Count.ShouldBe(1);

        await RunAtAsync(api, Evening.AddDays(1));
        (await SentAsync(api)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sealed_registers_are_not_reminded()
    {
        await using var api = await StartUpAsync();
        await SealAsync(api, await RecordAsync(api, StoreA, "Kasse 1"));
        await SealAsync(api, await RecordAsync(api, StoreB, "Kasse 1"));

        await RunAtAsync(api, Evening);

        (await SentAsync(api)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Switched_off_nobody_is_reminded_and_switching_on_again_works()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreB, "Kasse 1");
        using (var off = await api.SendAsync(HttpMethod.Put, "/api/cash-registers/reminder-settings", Hanna, new { enabled = false, time = "18:00" }))
        {
            off.StatusCode.ShouldBe(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        }

        await RunAtAsync(api, Evening);
        (await SentAsync(api)).ShouldBeEmpty();

        using var on = await api.SendAsync(HttpMethod.Put, "/api/cash-registers/reminder-settings", Hanna, new { enabled = true, time = "18:00" });
        await RunAtAsync(api, Evening.AddMinutes(5));
        (await SentAsync(api)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_time_of_the_settings_decides_when_it_goes_out()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreB, "Kasse 1");
        using (var early = await api.SendAsync(HttpMethod.Put, "/api/cash-registers/reminder-settings", Hanna, new { enabled = true, time = "08:30" }))
        {
            early.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await RunAtAsync(api, new(2026, 10, 24, 6, 29, 0, TimeSpan.Zero)); // 08:29 local (the next morning: the clock never goes back)
        (await SentAsync(api)).ShouldBeEmpty();
        await RunAtAsync(api, new(2026, 10, 24, 6, 30, 0, TimeSpan.Zero)); // 08:30 local
        (await SentAsync(api)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_nobody_to_tell_nothing_is_marked_and_the_next_round_tells_the_new_manager()
    {
        await using var api = await StartUpAsync(districtWithB: false); // store B has no district, store A no manager yet
        api.Grant(Erika); // Erika loses the privilege
        await RecordAsync(api, StoreA, "Kasse 1");

        await RunAtAsync(api, Evening);
        (await SentAsync(api)).ShouldBeEmpty();

        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterRecord, StoreIds: [StoreA]));
        await RunAtAsync(api, Evening.AddMinutes(15));
        (await SentAsync(api)).ShouldHaveSingleItem().RecipientEmployeeIds.ShouldBe([Erika]);
    }

    [Fact]
    public async Task Somebody_with_the_privilege_everywhere_is_no_store_manager()
    {
        await using var api = await StartUpAsync(districtWithB: false);
        api.Grant(Erika); // only Hanna holds the privilege, and everywhere
        await RecordAsync(api, StoreA, "Kasse 1");

        await RunAtAsync(api, Evening);

        (await SentAsync(api)).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ settings

    [Fact]
    public async Task The_settings_start_on_at_18_00_and_can_be_read_and_changed_with_the_privilege()
    {
        await using var api = await StartUpAsync();
        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterManageReminders, Unrestricted: true));

        using var read = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/reminder-settings", Erika);
        var defaults = await DataAsync(read);
        defaults.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        defaults.GetProperty("time").GetString().ShouldBe("18:00");

        using var save = await api.SendAsync(HttpMethod.Put, "/api/cash-registers/reminder-settings", Erika, new { enabled = false, time = "07:45" });
        var saved = await DataAsync(save);
        saved.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        saved.GetProperty("time").GetString().ShouldBe("07:45");
        using var again = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/reminder-settings", Erika);
        (await DataAsync(again)).GetProperty("time").GetString().ShouldBe("07:45");
    }

    [Theory]
    [InlineData("18")]
    [InlineData("25:00")]
    [InlineData("6:30 Uhr")]
    [InlineData("")]
    public async Task A_wrong_time_is_refused(string time)
    {
        await using var api = await StartUpAsync();

        using var response = await api.SendAsync(HttpMethod.Put, "/api/cash-registers/reminder-settings", Hanna, new { enabled = true, time });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("time");
    }

    [Fact]
    public async Task Switching_on_or_off_is_required()
    {
        await using var api = await StartUpAsync();

        using var response = await api.SendAsync(HttpMethod.Put, "/api/cash-registers/reminder-settings", Hanna, new { time = "18:00" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Without_the_privilege_the_settings_are_closed_even_to_a_district_leader()
    {
        await using var api = await StartUpAsync();

        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/reminder-settings", null);
        using var nobody = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/reminder-settings", Erika);
        using var leader = await api.SendAsync(HttpMethod.Put, "/api/cash-registers/reminder-settings", Ben, new { enabled = false, time = "18:00" });

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        nobody.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        leader.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ insight of the district leaders

    [Fact]
    public async Task A_district_leader_always_sees_the_stores_of_the_district_even_without_the_privilege()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreA, "Kasse A");
        await RecordAsync(api, StoreB, "Kasse B");

        using var list = await api.SendAsync(HttpMethod.Get, "/api/cash-registers", Ben);
        list.StatusCode.ShouldBe(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var data = await DataAsync(list);

        data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ShouldBe(["Kasse B"]);
        data.GetProperty("summary").GetProperty("stores").GetInt32().ShouldBe(1);
        using var history = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/history", Ben);
        (await DataAsync(history)).GetArrayLength().ShouldBe(1);
        using var foreign = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreA}", Ben);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_new_store_in_the_district_shows_up_without_any_change_of_privileges()
    {
        await using var api = await StartUpAsync();
        await RecordAsync(api, StoreC, "Kasse C");
        var structure = new
        {
            districts = new object[] { new { id = District, name = "Süd", leaderIds = new[] { Ben } } },
            regions = Array.Empty<object>(),
            stores = new object[]
            {
                new { storeId = StoreB, districtId = District, regionId = (Guid?)null },
                new { storeId = StoreC, districtId = District, regionId = (Guid?)null },
            },
        };
        (await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", Hanna, structure)).Dispose();

        using var list = await api.SendAsync(HttpMethod.Get, "/api/cash-registers", Ben);

        (await DataAsync(list)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ShouldBe(["Kasse C"]);
    }

    [Fact]
    public async Task Insight_is_not_the_right_to_change_anything()
    {
        await using var api = await StartUpAsync();
        var id = await RecordAsync(api, StoreB, "Kasse B");

        using var seal = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Ben);
        using var delete = await api.SendAsync(HttpMethod.Delete, $"/api/cash-registers/{id}", Ben);
        using var create = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Ben, new { storeId = StoreB, name = "Neu" });

        seal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Somebody_who_leads_nothing_and_has_no_privilege_sees_nothing()
    {
        await using var api = await StartUpAsync();
        api.Grant(Ben); // not a district leader for this test: remove from the structure
        var structure = new { districts = new object[] { new { id = District, name = "Süd", leaderIds = Array.Empty<Guid>() } }, regions = Array.Empty<object>(), stores = Array.Empty<object>() };
        (await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", Hanna, structure)).Dispose();

        using var list = await api.SendAsync(HttpMethod.Get, "/api/cash-registers", Ben);

        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
