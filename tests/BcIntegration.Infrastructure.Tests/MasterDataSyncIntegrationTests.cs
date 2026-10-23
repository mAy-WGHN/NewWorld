using BcIntegration.Contracts;
using BcIntegration.Core.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>Master data comparison against the BC fake: events only for real changes, all committed together.</summary>
public sealed class MasterDataSyncIntegrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task RunAsync(BcTestHost bc)
    {
        await using var scope = bc.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MasterDataSynchronizer>().SyncAsync(CancellationToken.None);
    }

    private static async Task<List<OutboxMessage>> Outbox(string connectionString)
    {
        await using var db = BcDatabase.Open(connectionString);
        return await db.Set<OutboxMessage>().ToListAsync();
    }

    [Fact]
    public async Task Initial_load_publishes_stores_and_employees_with_resolved_home_store()
    {
        var cs = await BcDatabase.CreateAsync(postgres);
        await using var bc = BcTestHost.Start("master-data", connectionString: cs);

        await RunAsync(bc);

        var outbox = await Outbox(cs);
        outbox.Select(m => m.MessageType).Order().ShouldBe(["EmployeeChanged", "EmployeeChanged", "StoreChanged"]);
        var store = MessageJson.Deserialize<StoreChanged>(outbox.Single(m => m.MessageType == "StoreChanged").Payload);
        var employees = outbox.Where(m => m.MessageType == "EmployeeChanged").Select(m => MessageJson.Deserialize<EmployeeChanged>(m.Payload)).ToList();
        employees.Single(e => e.PersonnelNumber == "10042").HomeStoreId.ShouldBe(store.StoreId);
        employees.Single(e => e.PersonnelNumber == "10043").HomeStoreId.ShouldBeNull();
        outbox.ShouldAllBe(m => m.Destination == MessagingNames.FromTypeName(m.MessageType));
    }

    [Fact]
    public async Task Second_run_with_unchanged_data_publishes_nothing_and_asks_only_for_changes()
    {
        var cs = await BcDatabase.CreateAsync(postgres);
        await using var bc = BcTestHost.Start("master-data", connectionString: cs);
        await RunAsync(bc);

        await RunAsync(bc);

        (await Outbox(cs)).Count.ShouldBe(3);
        var secondEmployeeQuery = bc.Server.LogEntries
            .Where(e => e.RequestMessage!.Path.EndsWith("/employees", StringComparison.Ordinal))
            .Select(e => e.RequestMessage!.Query is { } q && q.TryGetValue("$filter", out var filter) ? filter.ToString() : null)
            .Last();
        secondEmployeeQuery.ShouldBe("lastModifiedDateTime gt 2026-12-31T22:00:00.000Z"); // watermark 22:05 minus 5 minutes overlap
    }
}
