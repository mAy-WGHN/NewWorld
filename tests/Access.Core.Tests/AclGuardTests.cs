using Access.Core.Acl;
using static Access.Core.Tests.World;

namespace Access.Core.Tests;

/// <summary>Delegated administration never grants more than the actor has (archive: AclGuard, SecurityTests).</summary>
public class AclGuardTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    private readonly World _world = new();

    public AclGuardTests()
    {
        _world.People.Add(new(Admin, "1", "Admin", null, null, null));
        _world.Assignments.Add(Assignment.Unrestricted(Admin, Role.AdministratorId));
        // Max may manage assignments in his store.
        _world.Roles.Add(new Role(Guid.Parse("10000000-0000-0000-0000-000000000009"), "Filialverwaltung",
            new HashSet<string> { "access:assignment:create", "access:assignment:delete", "access:responsibility:create", "access:main-store:update" }, false));
        _world.Assignments.Add(Assignment.Restricted(Max, Guid.Parse("10000000-0000-0000-0000-000000000009"), includeHomeStore: true));
    }

    private AclGuard Guard() => new(_world.Data(), Now, Berlin);

    [Fact]
    public void The_main_store_can_be_set_for_people_and_stores_in_reach_only()
    {
        Guard().CanSetMainStore(Max, Erika, F017).ShouldBe(GuardResult.Allowed);
        Guard().CanSetMainStore(Max, Erika, null).ShouldBe(GuardResult.Allowed); // removal: the BC suggestion applies again
        Guard().CanSetMainStore(Max, Erika, F018).ShouldBe(GuardResult.Denied(GuardReason.NotInReach)); // store outside the reach
        Guard().CanSetMainStore(Max, Tom, F017).ShouldBe(GuardResult.Denied(GuardReason.NotInReach)); // person outside the reach
        Guard().CanSetMainStore(Erika, Erika, F017).ShouldBe(GuardResult.Denied(GuardReason.NotInReach)); // no privilege
        Guard().CanSetMainStore(Admin, Tom, F017).ShouldBe(GuardResult.Allowed);
    }

    [Fact]
    public void Store_manager_may_grant_his_own_role_within_his_store()
    {
        Guard().CanAssign(Max, Erika, StoreManagerRole, AssignmentReach.Scopes([StoreF017Scope]), validTo: null).ShouldBe(GuardResult.Allowed);
    }

    [Fact]
    public void Granting_more_reach_than_one_has_is_refused()
    {
        Guard().CanAssign(Max, Erika, StoreManagerRole, AssignmentReach.Scopes([DistrictSouth]), null).ShouldBe(GuardResult.Denied(GuardReason.NoEscalation));
        Guard().CanAssign(Max, Erika, StoreManagerRole, AssignmentReach.Everywhere, null).ShouldBe(GuardResult.Denied(GuardReason.NoEscalation));
    }

    [Fact]
    public void Granting_privileges_one_does_not_have_is_refused() =>
        Guard().CanAssign(Max, Erika, HrRole, AssignmentReach.Scopes([StoreF017Scope]), null).ShouldBe(GuardResult.Denied(GuardReason.NoEscalation));

    [Fact]
    public void Assigning_people_outside_ones_reach_is_refused() =>
        Guard().CanAssign(Max, Tom, StoreManagerRole, AssignmentReach.Scopes([StoreF017Scope]), null).ShouldBe(GuardResult.Denied(GuardReason.NotInReach));

    [Fact]
    public void Without_the_management_privilege_nothing_can_be_granted() =>
        Guard().CanAssign(Erika, Erika, Role.PublicUserId, AssignmentReach.Everywhere, null).ShouldBe(GuardResult.Denied(GuardReason.NotInReach));

    [Fact]
    public void A_substitute_cannot_grant_beyond_his_own_end_date()
    {
        // Tom substitutes in F017 until 15 Aug and gets the management role for that period only.
        _world.Assignments.Add(Assignment.Restricted(Tom, Guid.Parse("10000000-0000-0000-0000-000000000009"), false, [StoreF017Scope],
            validFrom: null, validTo: new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(2))));
        var during = new AclGuard(_world.Data(), new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.FromHours(2)), Berlin);

        during.CanAssign(Tom, Erika, StoreManagerRole, AssignmentReach.Scopes([StoreF017Scope]), validTo: null)
            .ShouldBe(GuardResult.Denied(GuardReason.ValidityTooLong));
        during.CanAssign(Tom, Erika, StoreManagerRole, AssignmentReach.Scopes([StoreF017Scope]), new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.FromHours(2)))
            .ShouldBe(GuardResult.Allowed);
    }

    [Fact]
    public void Administrator_may_grant_anything() =>
        Guard().CanAssign(Admin, Erika, HrRole, AssignmentReach.Everywhere, null).ShouldBe(GuardResult.Allowed);

    [Fact]
    public void Built_in_administrator_role_cannot_be_changed_or_deleted()
    {
        Guard().CanChangeRole(Admin, Role.AdministratorId, new HashSet<string> { "faq:read" }).ShouldBe(GuardResult.Denied(GuardReason.BuiltInRole));
        Guard().CanDeleteRole(Admin, Role.AdministratorId).ShouldBe(GuardResult.Denied(GuardReason.BuiltInRole));
        Guard().CanDeleteRole(Admin, Role.PublicUserId).ShouldBe(GuardResult.Denied(GuardReason.BuiltInRole));
    }

    [Fact]
    public void Changing_a_role_requires_covering_old_and_new_privileges_everywhere()
    {
        Guard().CanChangeRole(Max, StoreManagerRole, new HashSet<string> { "absence:read" }).ShouldBe(GuardResult.Denied(GuardReason.NoEscalation));
        Guard().CanChangeRole(Petra, StoreManagerRole, new HashSet<string> { "absence:read" }).ShouldBe(GuardResult.Denied(GuardReason.NoEscalation)); // lacks access:role:update
        Guard().CanChangeRole(Admin, StoreManagerRole, new HashSet<string> { "absence:read" }).ShouldBe(GuardResult.Allowed);
    }

    [Fact]
    public void Unknown_privileges_cannot_be_put_into_roles() =>
        Guard().CanChangeRole(Admin, StoreManagerRole, new HashSet<string> { "report:read" }).ShouldBe(GuardResult.Denied(GuardReason.UnknownPrivilege));

    [Fact]
    public void Removing_the_last_permanent_administrator_is_refused()
    {
        var adminAssignment = _world.Assignments.Single(a => a.EmployeeId == Admin);

        Guard().CanRemoveAssignment(Admin, adminAssignment.Id).ShouldBe(GuardResult.Denied(GuardReason.LastAdministrator));

        _world.People.Add(new(Guid.Parse("00000000-0000-0000-0000-0000000000fe"), "2", "Admin 2", null, null, null));
        _world.Assignments.Add(Assignment.Unrestricted(Guid.Parse("00000000-0000-0000-0000-0000000000fe"), Role.AdministratorId));
        Guard().CanRemoveAssignment(Admin, adminAssignment.Id).ShouldBe(GuardResult.Allowed);
    }

    [Fact]
    public void Time_limited_administrator_does_not_count_as_remaining()
    {
        var second = Guid.Parse("00000000-0000-0000-0000-0000000000fe");
        _world.People.Add(new(second, "2", "Admin 2", null, null, null));
        _world.Assignments.Add(Assignment.Unrestricted(second, Role.AdministratorId, validTo: Now.AddDays(1)));

        Guard().CanRemoveAssignment(Admin, _world.Assignments.Single(a => a.EmployeeId == Admin).Id).ShouldBe(GuardResult.Denied(GuardReason.LastAdministrator));
    }

    [Fact]
    public void Changing_scope_members_requires_reach_over_the_touched_stores()
    {
        _world.Roles.Add(new Role(Guid.NewGuid(), "Scope-Pflege", new HashSet<string> { "access:scope:update" }, false));
        _world.Assignments.Add(Assignment.Restricted(Sabine, _world.Roles[^1].Id, false, [DistrictSouth]));

        Guard().CanChangeScope(Sabine, StoreF017Scope, new HashSet<Guid> { F017, F018 }).ShouldBe(GuardResult.Allowed);
        Guard().CanChangeScope(Sabine, StoreF017Scope, new HashSet<Guid> { F017, Guid.NewGuid() }).ShouldBe(GuardResult.Denied(GuardReason.NotInReach));
    }

    [Fact]
    public void Responsibility_can_be_set_only_within_ones_reach()
    {
        Guard().CanSetResponsibility(Max, Responsibilities.ResponsibilityTarget.ForStore(F017)).ShouldBe(GuardResult.Allowed);
        Guard().CanSetResponsibility(Max, Responsibilities.ResponsibilityTarget.ForStore(F018)).ShouldBe(GuardResult.Denied(GuardReason.NotInReach));
        Guard().CanSetResponsibility(Max, Responsibilities.ResponsibilityTarget.ForEmployee(Erika)).ShouldBe(GuardResult.Allowed);
        Guard().CanSetResponsibility(Max, Responsibilities.ResponsibilityTarget.ForScope(DistrictSouth)).ShouldBe(GuardResult.Denied(GuardReason.NotInReach));
    }
}
