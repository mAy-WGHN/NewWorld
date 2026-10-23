using Access.Contracts;
using Access.Core.Acl;
using Access.Core.Messaging;
using Access.Core.Publishing;
using Access.Core.Responsibilities;
using Microsoft.Extensions.Time.Testing;
using Shared.Messaging;
using static Access.Core.Tests.World;

namespace Access.Core.Tests;

public class RecalculatorTests
{
    private readonly World _world = new();
    private readonly FakePublicationStore _publications = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 31, 21, 0, 0, TimeSpan.Zero)); // 23:00 local

    public RecalculatorTests() =>
        _world.Responsibilities.Add(Responsibility.Create("absence.approval", Max, ResponsibilityTarget.ForStore(F017)));

    private Recalculator Recalculator() => new(new FakeReader(_world), _publications, _publisher, _time, new AccessOptions());

    private IEnumerable<PermissionsChanged> PermissionsFor(Guid employeeId) =>
        _publisher.Events.OfType<PermissionsChanged>().Where(p => p.EmployeeId == employeeId);

    [Fact]
    public async Task First_run_publishes_version_one_for_everybody()
    {
        var result = await Recalculator().RecalculateAllAsync(CancellationToken.None);

        result.Permissions.Count.ShouldBe(5);
        _publisher.Events.OfType<PermissionsChanged>().ShouldAllBe(p => p.Version == 1);
        _publisher.Events.OfType<ResponsibilitiesChanged>().Count().ShouldBe(5);
        var max = PermissionsFor(Max).Single();
        max.Privileges.Select(p => p.Key).ShouldBe(["absence:approve", "absence:read", "absence:read-own", "absence:read-reason", "faq:read"]);
        max.Privileges.Single(p => p.Key == "absence:approve").StoreIds.ShouldBe([F017]);
        max.RoleAdditions.ShouldBe([Role.PublicUserName, "Filialleitung"]);
        max.ExternalIdentities.ShouldBeEmpty();
        _publisher.Events.OfType<ResponsibilitiesChanged>().Single(r => r.EmployeeId == Erika)
            .Responsibilities.ShouldHaveSingleItem().ResponsibleEmployeeIds.ShouldBe([Max]);
    }

    [Fact]
    public async Task Unchanged_state_publishes_nothing()
    {
        await Recalculator().RecalculateAllAsync(CancellationToken.None);
        _publisher.Events.Clear();

        var result = await Recalculator().RecalculateAllAsync(CancellationToken.None);

        result.Permissions.ShouldBeEmpty();
        _publisher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_of_a_substitution_publishes_a_new_version_for_the_substitute_only()
    {
        await Recalculator().RecalculateAllAsync(CancellationToken.None);
        _publisher.Events.Clear();

        _time.Advance(TimeSpan.FromHours(1)); // 2026-08-01 00:00 local: Tom starts substituting in F017
        await Recalculator().RecalculateAllAsync(CancellationToken.None);

        var tom = _publisher.Events.OfType<PermissionsChanged>().ShouldHaveSingleItem();
        tom.EmployeeId.ShouldBe(Tom);
        tom.Version.ShouldBe(2);
        tom.Privileges.Single(p => p.Key == "absence:approve").StoreIds.ShouldBe([F017, F018], ignoreOrder: true);
        tom.OccurredAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Exit_publishes_an_empty_set_at_the_end_of_the_exit_date()
    {
        _world.People[1] = _world.People[1] with { ExitDate = new DateOnly(2026, 7, 31) };
        await Recalculator().RecalculateAllAsync(CancellationToken.None);
        PermissionsFor(Max).Single().Privileges.ShouldNotBeEmpty(); // received in advance, still active
        _publisher.Events.Clear();

        _time.Advance(TimeSpan.FromHours(1));
        await Recalculator().RecalculateAllAsync(CancellationToken.None);

        var max = PermissionsFor(Max).ShouldHaveSingleItem();
        max.Privileges.ShouldBeEmpty();
        max.Version.ShouldBe(2);
        _publisher.Events.OfType<ResponsibilitiesChanged>().Single(r => r.EmployeeId == Erika).Responsibilities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Versions_rise_strictly_per_person()
    {
        await Recalculator().RecalculateAllAsync(CancellationToken.None);
        _world.Assignments.RemoveAll(a => a.EmployeeId == Max);
        await Recalculator().RecalculateAllAsync(CancellationToken.None);
        _world.Assignments.Add(Assignment.Restricted(Max, StoreManagerRole, includeHomeStore: true));
        await Recalculator().RecalculateAllAsync(CancellationToken.None);

        PermissionsFor(Max).Select(p => p.Version).ShouldBe([1L, 2L, 3L]);
    }

    private sealed class FakeReader(World world) : IAclReader
    {
        public Task<AclData> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(world.Data());
    }

    private sealed class FakePublicationStore : IPublicationStore
    {
        private readonly Dictionary<(Guid, PublicationKind), Publication> _rows = [];

        public Task<IReadOnlyDictionary<(Guid EmployeeId, PublicationKind Kind), Publication>> LoadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<(Guid EmployeeId, PublicationKind Kind), Publication>>(new Dictionary<(Guid, PublicationKind), Publication>(_rows));

        public Task SaveAsync(Guid employeeId, PublicationKind kind, Publication publication, CancellationToken cancellationToken)
        {
            _rows[(employeeId, kind)] = publication;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<IEvent> Events { get; } = [];

        public void Publish(IEvent message) => Events.Add(message);
    }
}
