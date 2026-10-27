using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using Workforce.Api;
using Workforce.Infrastructure;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

public sealed class PeopleLifecycleTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Employee_changed_creates_the_copy_and_ignores_unknown_json_fields()
    {
        await using var api = await StartAsync(postgres);
        var id = Guid.NewGuid();
        var message = new EmployeeChanged(Guid.NewGuid(), Guid.NewGuid(), Start, id, "4711", "Neu Mitarbeiter", StoreA, null, null, null, null, null);
        var json = MessageJson.Serialize(message).TrimEnd('}') + ",\"futureField\":42}";

        var result = await api.Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(message.MessageId.ToString(), nameof(EmployeeChanged), json, 1), CancellationToken.None);

        result.Outcome.ShouldBe(DispatchOutcome.Completed);
        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().People.AsNoTracking().SingleAsync(p => p.EmployeeId == id)).Name.ShouldBe("Neu Mitarbeiter");
    }

    [Fact]
    public async Task The_main_store_from_access_wins_over_the_suggestion_from_bc_and_older_versions_are_ignored()
    {
        await using var api = await StartAsync(postgres);
        var id = Guid.NewGuid();
        Task Bc(Guid? store) => api.DeliverAsync(new EmployeeChanged(Guid.NewGuid(), Guid.NewGuid(), Start, id, "4711", "Neu Mitarbeiter", store, null, null, null, null, null));
        Task Access(long version, Guid? store) => api.DeliverAsync(new PermissionsChanged(Guid.NewGuid(), Guid.NewGuid(), Start, id, version, [], [], [], [], [], store));
        async Task<(Guid? Store, Guid? Bc)> Person()
        {
            using var scope = api.Services.CreateScope();
            var p = await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().People.AsNoTracking().SingleAsync(x => x.EmployeeId == id);
            return (p.HomeStoreId, p.BcHomeStoreId);
        }

        // Access tells the main store before the person's copy exists: it is used as soon as the copy arrives.
        await Access(1, StoreB);
        await Bc(StoreA);
        (await Person()).ShouldBe((StoreB, StoreA));

        // BC changes its suggestion: the maintained main store stays.
        await Bc(null);
        (await Person()).ShouldBe((StoreB, null));

        // An older message from Access is ignored; a newer one is taken over.
        await Access(0, StoreA);
        (await Person()).Store.ShouldBe(StoreB);
        await Access(2, StoreA);
        (await Person()).Store.ShouldBe(StoreA);

        // Access has no main store at all (null): the suggestion from BC applies.
        await Bc(StoreB);
        await Access(3, null);
        (await Person()).ShouldBe((StoreB, StoreB));
    }

    [Fact]
    public async Task Employee_left_ends_an_open_sick_note_at_the_exit_date()
    {
        await using var api = await StartAsync(postgres);
        var open = await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(new DateOnly(2026, 10, 23), null));

        await api.DeliverAsync(new EmployeeLeft(Guid.NewGuid(), Guid.NewGuid(), Start, Erika, new DateOnly(2026, 10, 30)));

        var absence = (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == open.Id);
        absence.To.ShouldBe(new DateOnly(2026, 10, 30));
    }

    [Fact]
    public async Task Erasure_leaves_no_personal_data_behind()
    {
        await using var api = await StartAsync(postgres);
        await api.SetEntitlementAsync(Erika, 2026, 30);
        await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 24)));
        await api.PostAbsenceAsync("/api/me/absences/vacation", Erika, new RequestVacationRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)));

        var result = await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, Erika));

        result.Outcome.ShouldBe(DispatchOutcome.Completed);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        (await db.Absences.AnyAsync(a => a.EmployeeId == Erika)).ShouldBeFalse();
        (await db.Entitlements.AnyAsync(e => e.EmployeeId == Erika)).ShouldBeFalse();
        (await db.People.AnyAsync(p => p.EmployeeId == Erika)).ShouldBeFalse();
        (await db.MainStoreCopies.AnyAsync(m => m.EmployeeId == Erika)).ShouldBeFalse();
        (await db.ApproverCopies.AnyAsync(a => a.EmployeeId == Erika)).ShouldBeFalse();
        (await db.Set<Shared.Approvals.ApprovalRequest>().AnyAsync(r => r.RequesterId == Erika)).ShouldBeFalse();
        (await api.OutboxAsync()).Where(m => m.Payload.Contains(Erika.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(m => m.MessageType).ShouldBeEmpty("outgoing messages about the person are deleted too");
        // Colleagues still exist.
        (await db.People.AnyAsync(p => p.EmployeeId == Ben)).ShouldBeTrue();
    }

    [Fact]
    public async Task Responsibilities_changed_replaces_the_approvers_and_ignores_older_versions()
    {
        await using var api = await StartAsync(postgres);
        var newer = new ResponsibilitiesChanged(Guid.NewGuid(), Guid.NewGuid(), Start, Erika, 5, [new TopicResponsibility("absence.approval", [Lena])]);
        var older = new ResponsibilitiesChanged(Guid.NewGuid(), Guid.NewGuid(), Start, Erika, 3, [new TopicResponsibility("absence.approval", [Max])]);

        await api.DeliverAsync(newer);
        await api.DeliverAsync(older);

        using var scope = api.Services.CreateScope();
        var copy = await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().ApproverCopies.AsNoTracking().SingleAsync(a => a.EmployeeId == Erika);
        copy.ApproverIds.ShouldBe([Lena]);
        copy.Version.ShouldBe(5);
    }
}
