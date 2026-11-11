using System.Net;
using Access.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Testing;
using Workforce.Api;
using Workforce.Infrastructure;
using Workforce.Infrastructure.WorkingTime;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

/// <summary>With several instances one round of the periodic work runs on one instance only: if another instance holds the lock, the round is skipped.</summary>
public sealed class MaintenanceLockTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    /// <summary>Erika left three days ago with a positive balance and HR is responsible: the payout reminder is due.</summary>
    private static async Task PrepareDueReminderAsync(WorkforceApi api)
    {
        (await api.SendAsync(HttpMethod.Post, $"/api/employees/{Erika}/working-time/corrections", Hanna, new BookHoursRequest(7.5m, new DateOnly(2026, 10, 1)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
            (await db.People.SingleAsync(p => p.EmployeeId == Erika)).ExitDate = new DateOnly(2026, 10, 20);
            await db.SaveChangesAsync();
        }

        await api.DeliverAsync(new ResponsibilitiesChanged(Guid.NewGuid(), Guid.NewGuid(), Start, Erika, 5, [new TopicResponsibility("workingtime.payout", [Max])]));
    }

    private static WorkingTimeMaintenanceService Maintenance(WorkforceApi api) =>
        api.Services.GetServices<IHostedService>().OfType<WorkingTimeMaintenanceService>().Single();

    private static async Task<int> RemindersAsync(WorkforceApi api) =>
        (await api.OutboxAsync()).Count(m => m.Payload.Contains("workforce.working-time-payout-reminder.hr", StringComparison.Ordinal));

    [Fact]
    public async Task A_round_is_skipped_while_another_instance_holds_the_lock_and_nothing_is_marked()
    {
        await using var busy = await StartAsync(postgres, services => services.AddSingleton<Shared.Infrastructure.Persistence.IDistributedLock, BusyLock>());
        await PrepareDueReminderAsync(busy);

        await Maintenance(busy).RunOnceAsync(CancellationToken.None);

        (await RemindersAsync(busy)).ShouldBe(0);
    }

    [Fact]
    public async Task A_round_runs_when_the_lock_is_free()
    {
        await using var free = await StartAsync(postgres);
        await PrepareDueReminderAsync(free);

        await Maintenance(free).RunOnceAsync(CancellationToken.None);

        (await RemindersAsync(free)).ShouldBe(1);
    }
}
