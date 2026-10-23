using Npgsql;

namespace AppHost.Tests;

/// <summary>
/// E2E: after the start, BC Integration compares master data with the WireMock BC and records the events in its outbox.
/// (The distribution to the other services is covered by AbsenceE2ETests.)
/// </summary>
[Trait("Category", "E2E")]
[Collection(AppHostGroup.Name)]
public class BcIntegrationE2ETests(AppHostFixture fixture)
{
    [Fact]
    public async Task Master_data_from_the_bc_fake_becomes_events_of_bc_integration()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync("bc-integration-api", cts.Token);
        var connectionString = await fixture.App.GetConnectionStringAsync("bcintegration", cts.Token);

        var types = new List<string>();
        while (types.Count < 7 && !cts.IsCancellationRequested)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cts.Token);
            await using var command = new NpgsqlCommand("select message_type from bc_integration.outbox_messages order by message_type", connection);
            types.Clear();
            await using var reader = await command.ExecuteReaderAsync(cts.Token);
            while (await reader.ReadAsync(cts.Token))
            {
                types.Add(reader.GetString(0));
            }

            if (types.Count < 7)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }
        }

        types.ShouldBe(["EmployeeChanged", "EmployeeChanged", "EmployeeChanged", "EmployeeChanged", "EmployeeChanged", "EmployeeChanged", "StoreChanged"]);
    }
}
