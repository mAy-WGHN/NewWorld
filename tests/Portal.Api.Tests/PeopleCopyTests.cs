using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Infrastructure;
using Shared.Infrastructure.Messaging;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The Portal's copy of a person follows BC (suggested main store, entry and exit) and Access (main store maintained there wins),
/// the same way as in Workforce.
/// </summary>
public sealed class PeopleCopyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Newcomer = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static EmployeeChanged Changed(Guid id, Guid? store, DateOnly? entry = null, DateOnly? exit = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Start, id, "9001", "Nina Neu", store, null, null, null, entry, exit);

    private static PermissionsChanged Access(Guid id, long version, Guid? store) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Start, id, version, [], [], [], [], [], store);

    private sealed record PersonView(Guid? HomeStoreId, DateOnly? EntryDate, DateOnly? ExitDate);

    private static async Task<PersonView> PersonAsync(PortalApi api, Guid id)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var p = await db.People.AsNoTracking().SingleAsync(x => x.EmployeeId == id);
        return new(p.HomeStoreId, p.EntryDate, p.ExitDate);
    }

    [Fact]
    public async Task The_main_store_from_Access_wins_over_the_suggestion_of_BC_in_either_order()
    {
        await using var api = await StartAsync(postgres);

        // Access first, then BC: the person does not exist yet when Access publishes.
        await api.DeliverAsync(Access(Newcomer, 1, StoreB));
        await api.DeliverAsync(Changed(Newcomer, StoreA));
        (await PersonAsync(api, Newcomer)).HomeStoreId.ShouldBe(StoreB);

        // BC changes its suggestion: the maintained one stays.
        await api.DeliverAsync(Changed(Newcomer, StoreC));
        (await PersonAsync(api, Newcomer)).HomeStoreId.ShouldBe(StoreB);

        // Access drops the maintained one: the suggestion of BC applies again.
        await api.DeliverAsync(Access(Newcomer, 2, null));
        (await PersonAsync(api, Newcomer)).HomeStoreId.ShouldBe(StoreC);
    }

    [Fact]
    public async Task Older_versions_of_PermissionsChanged_do_not_change_the_main_store()
    {
        await using var api = await StartAsync(postgres);
        await api.DeliverAsync(Changed(Newcomer, StoreA));
        await api.DeliverAsync(Access(Newcomer, 5, StoreB));

        await api.DeliverAsync(Access(Newcomer, 4, StoreC));

        (await PersonAsync(api, Newcomer)).HomeStoreId.ShouldBe(StoreB);
    }

    [Fact]
    public async Task EmployeeHired_records_the_entry_date_of_a_known_person_and_ignores_unknown_ones()
    {
        await using var api = await StartAsync(postgres);
        await api.DeliverAsync(Changed(Newcomer, StoreA));

        var hired = await api.DeliverAsync(new EmployeeHired(Guid.NewGuid(), Guid.NewGuid(), Start, Newcomer, new DateOnly(2026, 11, 1), StoreA));
        var unknown = await api.DeliverAsync(new EmployeeHired(Guid.NewGuid(), Guid.NewGuid(), Start, Guid.NewGuid(), new DateOnly(2026, 11, 1), StoreA));

        hired.Outcome.ShouldBe(DispatchOutcome.Completed);
        unknown.Outcome.ShouldBe(DispatchOutcome.Completed);
        (await PersonAsync(api, Newcomer)).EntryDate.ShouldBe(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public async Task EmployeeHired_does_not_overrule_the_main_store_from_Access()
    {
        await using var api = await StartAsync(postgres);
        await api.DeliverAsync(Changed(Newcomer, StoreA));
        await api.DeliverAsync(Access(Newcomer, 1, StoreB));

        await api.DeliverAsync(new EmployeeHired(Guid.NewGuid(), Guid.NewGuid(), Start, Newcomer, new DateOnly(2026, 11, 1), StoreC));

        (await PersonAsync(api, Newcomer)).HomeStoreId.ShouldBe(StoreB);
    }

    [Fact]
    public async Task EmployeeLeft_records_the_exit_date()
    {
        await using var api = await StartAsync(postgres);
        await api.DeliverAsync(Changed(Newcomer, StoreA));

        var result = await api.DeliverAsync(new EmployeeLeft(Guid.NewGuid(), Guid.NewGuid(), Start, Newcomer, new DateOnly(2026, 12, 31)));

        result.Outcome.ShouldBe(DispatchOutcome.Completed);
        (await PersonAsync(api, Newcomer)).ExitDate.ShouldBe(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task Erasure_removes_the_main_store_copy_too()
    {
        await using var api = await StartAsync(postgres);
        await api.DeliverAsync(Access(Newcomer, 1, StoreB));
        await api.DeliverAsync(Changed(Newcomer, StoreA));

        await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, Newcomer));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        (await db.People.AnyAsync(p => p.EmployeeId == Newcomer)).ShouldBeFalse();
        (await db.MainStoreCopies.AnyAsync(m => m.EmployeeId == Newcomer)).ShouldBeFalse();
    }
}
