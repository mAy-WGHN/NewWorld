using System.Net;
using Access.Api;
using Access.Contracts;
using Access.Core.Acl;
using Access.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>Positive paths of every endpoint, the protection rules through the API and the published events.</summary>
public sealed class AdministrationFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static Func<HttpRequestMessage, HttpRequestMessage> As(Guid person) => r => r.AsUser(person);

    [Fact]
    public async Task Bootstrap_administrator_has_everything_and_sees_it_in_me_privileges()
    {
        await using var api = await StartAsync(postgres);

        var me = await api.GetAsync<MyPrivilegesDto>("/api/me/privileges", As(Admin));

        me.Roles.ShouldContain(Role.AdministratorName);
        me.Privileges.ShouldContain(p => p.Key == AccessPrivileges.AssignmentCreate && p.Unrestricted);
    }

    [Fact]
    public async Task Store_manager_role_is_built_assigned_and_takes_effect_with_reach_and_event()
    {
        await using var api = await StartAsync(postgres);
        await api.RegisterWorkforceCatalogAsync();

        (await api.GetAsync<List<CatalogEntryDto>>("/api/catalog", As(Admin))).ShouldContain(c => c.Key == "absence:approve" && c.Owner == "workforce");
        var roleId = await api.CreateAsync("/api/roles", new CreateRoleRequest("Filialleitung", ["absence:read", "absence:approve", "access:assignment:create"]), As(Admin));
        (await api.GetAsync<List<RoleDto>>("/api/roles", As(Admin))).ShouldContain(r => r.Id == roleId);
        var assignmentId = await api.CreateAsync($"/api/people/{Max}/assignments", new CreateAssignmentRequest(roleId, false, [], true, null, null), As(Admin));

        var max = await api.GetAsync<MyPrivilegesDto>("/api/me/privileges", As(Max));
        var approve = max.Privileges.Single(p => p.Key == "absence:approve");
        approve.StoreIds.ShouldBe([F017]);
        approve.EmployeeIds.ShouldBe(new[] { Erika, Max }.Order().ToList());
        (await api.GetAsync<List<AssignmentDto>>($"/api/people/{Max}/assignments", As(Admin))).ShouldContain(a => a.Id == assignmentId);

        await using var scope = api.Services.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<AccessDbContext>().Set<OutboxMessage>()
            .Where(m => m.MessageType == nameof(PermissionsChanged)).ToListAsync();
        outbox.Select(m => MessageJson.Deserialize<PermissionsChanged>(m.Payload)).Where(p => p.EmployeeId == Max)
            .MaxBy(p => p.Version)!.Privileges.ShouldContain(p => p.Key == "absence:approve");

        (await api.SendAsync(HttpMethod.Delete, $"/api/assignments/{assignmentId}", As(Admin))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<MyPrivilegesDto>("/api/me/privileges", As(Max))).Privileges.ShouldNotContain(p => p.Key == "absence:approve");
    }

    [Fact]
    public async Task Store_manager_cannot_escalate_or_reach_beyond_his_store()
    {
        await using var api = await StartAsync(postgres);
        await api.RegisterWorkforceCatalogAsync();
        var managerRole = await api.CreateAsync("/api/roles", new CreateRoleRequest("Filialleitung", ["absence:read", "access:assignment:create"]), As(Admin));
        var hrRole = await api.CreateAsync("/api/roles", new CreateRoleRequest("HR", ["absence:*"]), As(Admin));
        await api.CreateAsync($"/api/people/{Max}/assignments", new CreateAssignmentRequest(managerRole, false, [], true, null, null), As(Admin));

        var escalation = await api.SendAsync(HttpMethod.Post, $"/api/people/{Erika}/assignments", As(Max), new CreateAssignmentRequest(hrRole, false, [], true, null, null));
        var otherStore = await api.SendAsync(HttpMethod.Post, $"/api/people/{Tom}/assignments", As(Max), new CreateAssignmentRequest(managerRole, false, [], true, null, null));
        var allowed = await api.SendAsync(HttpMethod.Post, $"/api/people/{Erika}/assignments", As(Max), new CreateAssignmentRequest(managerRole, false, [], true, null, null));

        escalation.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escalation.Content.ReadAsStringAsync()).ShouldContain(nameof(GuardReason.NoEscalation));
        otherStore.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await otherStore.Content.ReadAsStringAsync()).ShouldContain(nameof(GuardReason.NotInReach));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Last_administrator_cannot_be_removed()
    {
        await using var api = await StartAsync(postgres);
        var own = (await api.GetAsync<List<AssignmentDto>>($"/api/people/{Admin}/assignments", As(Admin))).Single();

        using var response = await api.SendAsync(HttpMethod.Delete, $"/api/assignments/{own.Id}", As(Admin));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain(nameof(GuardReason.LastAdministrator));
    }

    [Fact]
    public async Task Roles_scopes_and_custom_privileges_can_be_maintained()
    {
        await using var api = await StartAsync(postgres);
        await api.RegisterWorkforceCatalogAsync();

        var roleId = await api.CreateAsync("/api/roles", new CreateRoleRequest("Leser", ["absence:read"]), As(Admin));
        (await api.SendAsync(HttpMethod.Put, $"/api/roles/{roleId}", As(Admin), new UpdateRoleRequest(["absence:read", "absence:read-reason"]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Post, "/api/roles", As(Admin), new CreateRoleRequest("leser", []))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.SendAsync(HttpMethod.Put, $"/api/roles/{roleId}", As(Admin), new UpdateRoleRequest(["report:read"]))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}", As(Admin))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Delete, $"/api/roles/{Role.AdministratorId}", As(Admin))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var scopeId = await api.CreateAsync("/api/scopes", new CreateScopeRequest("Bezirk Süd", [F017]), As(Admin));
        (await api.SendAsync(HttpMethod.Put, $"/api/scopes/{scopeId}", As(Admin), new UpdateScopeRequest([F017, F018]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<List<ScopeDto>>("/api/scopes", As(Admin))).Single().StoreIds.Order().ShouldBe(new[] { F017, F018 }.Order());
        (await api.SendAsync(HttpMethod.Delete, $"/api/scopes/{scopeId}", As(Admin))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await api.SendAsync(HttpMethod.Post, "/api/custom-privileges", As(Admin), new CreateCustomPrivilegeRequest("revision:instruct", true))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<List<CatalogEntryDto>>("/api/catalog", As(Admin))).ShouldContain(c => c.Key == "revision:instruct" && c.Owner == "custom");
        (await api.SendAsync(HttpMethod.Delete, "/api/custom-privileges/revision:instruct", As(Admin))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Responsibility_per_topic_is_maintained_and_published()
    {
        await using var api = await StartAsync(postgres);

        var id = await api.CreateAsync("/api/responsibilities",
            new CreateResponsibilityRequest("absence.approval", Max, new ResponsibilityTargetDto("store", F017), null, null), As(Admin));
        var erika = await api.GetAsync<List<TopicResponsibilityDto>>($"/api/people/{Erika}/responsibilities", As(Admin));

        erika.ShouldHaveSingleItem().ResponsibleEmployeeIds.ShouldBe([Max]);
        var states = await api.GetAsync<List<ResponsibilityStateDto>>("/internal/responsibilities", r => r.AsService(AccessRoles.PermissionsRead));
        states.Single(s => s.EmployeeId == Erika).Responsibilities.ShouldHaveSingleItem().Topic.ShouldBe("absence.approval");
        (await api.SendAsync(HttpMethod.Post, "/api/responsibilities", As(Admin),
            new CreateResponsibilityRequest("Absence Approval", Max, new ResponsibilityTargetDto("store", F017), null, null))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await api.SendAsync(HttpMethod.Delete, $"/api/responsibilities/{id}", As(Admin))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<List<TopicResponsibilityDto>>($"/api/people/{Erika}/responsibilities", As(Admin))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Services_can_rebuild_their_copy_from_the_complete_current_state()
    {
        await using var api = await StartAsync(postgres);

        var states = await api.GetAsync<List<PermissionStateDto>>("/internal/permissions", r => r.AsService(AccessRoles.PermissionsRead));

        states.Select(s => s.EmployeeId).ShouldBe(new[] { Admin, Erika, Max, Tom }, ignoreOrder: true);
        states.Single(s => s.EmployeeId == Admin).Privileges.ShouldContain(p => p.Key == AccessPrivileges.RoleCreate && p.Unrestricted);
        states.ShouldAllBe(s => s.Version >= 1);
    }

    [Fact]
    public async Task Catalog_keys_belong_to_exactly_one_application()
    {
        await using var api = await StartAsync(postgres);
        await api.RegisterWorkforceCatalogAsync();

        using var portal = await api.SendAsync(HttpMethod.Put, "/internal/apps/portal/catalog", r => r.AsService(AccessRoles.CatalogRegister),
            new RegisterCatalogRequest([new("absence:read", true)]));
        using var reserved = await api.SendAsync(HttpMethod.Put, "/internal/apps/access/catalog", r => r.AsService(AccessRoles.CatalogRegister),
            new RegisterCatalogRequest([new("access:role:read", false)]));

        portal.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        reserved.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
