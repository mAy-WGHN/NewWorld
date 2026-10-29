using Access.Core.Acl;
using Access.Core.People;
using static Access.Core.Tests.World;

namespace Access.Core.Tests;

public class EffectivePermissionCalculatorTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateTimeOffset July = new(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly World _world = new();

    private EffectivePermissions For(Guid employeeId, DateTimeOffset at) =>
        EffectivePermissionCalculator.Calculate(employeeId, _world.Data(), at, Berlin);

    [Fact]
    public void Store_manager_reaches_the_home_store_and_its_employees()
    {
        var max = For(Max, July);

        max.Privileges["absence:approve"].StoreIds.ShouldBe([F017]);
        max.Privileges["absence:approve"].EmployeeIds.ShouldBe([Erika, Max], ignoreOrder: true);
        max.Privileges.ContainsKey("access:role:update").ShouldBeFalse();
    }

    [Fact]
    public void Everybody_active_gets_the_public_user_privileges()
    {
        var erika = For(Erika, July);

        erika.Privileges.Keys.ShouldBe(["absence:read-own", "faq:read"], ignoreOrder: true);
        erika.Privileges["faq:read"].Unrestricted.ShouldBeTrue();
        erika.Roles.ShouldBe([Role.PublicUserName]);
    }

    [Fact]
    public void District_manager_reaches_all_stores_of_the_district_scope()
    {
        var sabine = For(Sabine, July);

        sabine.Privileges["absence:read"].StoreIds.ShouldBe([F017, F018, F019], ignoreOrder: true);
        sabine.Privileges["absence:read"].EmployeeIds.ShouldBe([Erika, Max, Tom], ignoreOrder: true);
        sabine.Privileges.ContainsKey("absence:read-reason").ShouldBeFalse();
    }

    [Fact]
    public void Unrestricted_assignment_with_wildcard_expands_to_all_registered_matching_privileges()
    {
        var petra = For(Petra, July);

        petra.Privileges["absence:read-reason"].Unrestricted.ShouldBeTrue();
        petra.Privileges.Keys.Where(k => k.StartsWith("absence:", StringComparison.Ordinal)).Count().ShouldBe(4);
        petra.Privileges["access:assignment:create"].Unrestricted.ShouldBeTrue();
    }

    [Fact]
    public void Substitution_applies_only_within_its_period()
    {
        var before = For(Tom, new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.FromHours(2)));
        var during = For(Tom, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        var lastSecond = For(Tom, new DateTimeOffset(2026, 8, 14, 23, 59, 59, TimeSpan.FromHours(2)));
        var after = For(Tom, new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(2)));

        before.Privileges["absence:approve"].StoreIds.ShouldBe([F018]);
        during.Privileges["absence:approve"].StoreIds.ShouldBe([F017, F018], ignoreOrder: true);
        lastSecond.Privileges["absence:approve"].StoreIds.ShouldBe([F017, F018], ignoreOrder: true);
        after.Privileges["absence:approve"].StoreIds.ShouldBe([F018]);
    }

    [Fact]
    public void Non_scopable_privilege_counts_only_in_unrestricted_assignments()
    {
        _world.Roles.Add(new Role(Guid.NewGuid(), "FAQ-Pflege", new HashSet<string> { "faq:read" }, false));
        var role = _world.Roles[^1].Id;
        _world.Roles[1] = Role.PublicUser; // public user without privileges
        _world.Assignments.Add(Assignment.Restricted(Erika, role, includeHomeStore: true));

        For(Erika, July).Privileges.ContainsKey("faq:read").ShouldBeFalse();
    }

    [Fact]
    public void Restricted_assignment_without_scopes_or_home_store_reaches_nowhere()
    {
        _world.Assignments.Add(Assignment.Restricted(Erika, StoreManagerRole, includeHomeStore: false));

        For(Erika, July).Privileges.ContainsKey("absence:approve").ShouldBeFalse();
    }

    [Fact]
    public void Unregistered_privileges_have_no_effect()
    {
        _world.Roles.Add(new Role(Guid.NewGuid(), "Alt", new HashSet<string> { "report:read" }, false));
        _world.Assignments.Add(Assignment.Unrestricted(Erika, _world.Roles[^1].Id));

        For(Erika, July).Privileges.ContainsKey("report:read").ShouldBeFalse();
    }

    [Theory]
    [InlineData(2026, 3, 28, "2026-03-28T22:59:59Z", "2026-03-28T23:00:00Z")] // last day before summer time
    [InlineData(2026, 10, 24, "2026-10-24T21:59:59Z", "2026-10-24T22:00:00Z")] // last day before winter time
    [InlineData(2028, 2, 28, "2028-02-28T22:59:59Z", "2028-02-28T23:00:00Z")] // leap year: next day is 29 Feb
    [InlineData(2026, 12, 31, "2026-12-31T22:59:59Z", "2026-12-31T23:00:00Z")] // year end
    public void Exit_withdraws_everything_at_the_end_of_the_exit_date_local_time(int year, int month, int day, string stillActive, string withdrawn)
    {
        _world.People[1] = _world.People[1] with { ExitDate = new DateOnly(year, month, day) };

        For(Max, DateTimeOffset.Parse(stillActive, System.Globalization.CultureInfo.InvariantCulture)).Privileges.ShouldNotBeEmpty();
        For(Max, DateTimeOffset.Parse(withdrawn, System.Globalization.CultureInfo.InvariantCulture)).Privileges.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- after the exit: only the role for former employees

    private void FormerEmployeeMayRead(params string[] privileges) =>
        _world.Roles.Add(Role.FormerEmployee with { Privileges = new HashSet<string>(privileges) });

    [Fact]
    public void After_the_exit_a_person_keeps_only_what_the_role_for_former_employees_holds()
    {
        FormerEmployeeMayRead("absence:read-own");
        _world.People[0] = _world.People[0] with { ExitDate = new DateOnly(2026, 6, 30) };

        var erika = For(Erika, July);

        erika.Privileges.Keys.ShouldBe(["absence:read-own"]);   // not the public user privileges (faq:read), nothing else
        erika.Roles.ShouldBe([Role.FormerEmployeeName]);
        erika.Privileges["absence:read-own"].Unrestricted.ShouldBeTrue();
    }

    [Fact]
    public void Without_privileges_in_that_role_a_former_employee_has_nothing()
    {
        _world.Roles.Add(Role.FormerEmployee);
        _world.People[0] = _world.People[0] with { ExitDate = new DateOnly(2026, 6, 30) };

        For(Erika, July).Privileges.ShouldBeEmpty();
    }

    [Fact]
    public void Assignments_of_a_former_employee_no_longer_apply_not_even_the_store_reach()
    {
        FormerEmployeeMayRead("absence:read-own");
        _world.People[1] = _world.People[1] with { ExitDate = new DateOnly(2026, 6, 30) }; // Max, store manager

        var max = For(Max, July);

        max.Privileges.Keys.ShouldBe(["absence:read-own"]);
        max.StoreIds.ShouldBeEmpty();
    }

    [Fact]
    public void The_role_for_former_employees_does_not_apply_before_the_exit_and_is_not_added_for_active_people()
    {
        FormerEmployeeMayRead("absence:read-own", "absence:approve");

        var erika = For(Erika, July);

        erika.Roles.ShouldNotContain(Role.FormerEmployeeName);
        erika.Privileges.ContainsKey("absence:approve").ShouldBeFalse();
    }

    [Theory]
    [InlineData(2026, 3, 28, "2026-03-28T22:59:59Z", "2026-03-28T23:00:00Z")] // last day before summer time
    [InlineData(2026, 10, 24, "2026-10-24T21:59:59Z", "2026-10-24T22:00:00Z")] // last day before winter time
    [InlineData(2028, 2, 28, "2028-02-28T22:59:59Z", "2028-02-28T23:00:00Z")] // leap year
    [InlineData(2026, 12, 31, "2026-12-31T22:59:59Z", "2026-12-31T23:00:00Z")] // year end
    public void The_switch_to_the_former_employee_role_happens_at_the_end_of_the_exit_date_local_time(int year, int month, int day, string stillActive, string former)
    {
        FormerEmployeeMayRead("absence:read-own");
        _world.People[1] = _world.People[1] with { ExitDate = new DateOnly(year, month, day) };

        For(Max, DateTimeOffset.Parse(stillActive, System.Globalization.CultureInfo.InvariantCulture)).Privileges.ContainsKey("absence:approve").ShouldBeTrue();
        For(Max, DateTimeOffset.Parse(former, System.Globalization.CultureInfo.InvariantCulture)).Privileges.Keys.ShouldBe(["absence:read-own"]);
    }

    [Fact]
    public void Exit_date_known_in_advance_does_not_withdraw_on_receipt()
    {
        _world.People[1] = _world.People[1] with { ExitDate = new DateOnly(2026, 12, 31) };

        For(Max, July).Privileges["absence:approve"].StoreIds.ShouldBe([F017]);
    }

    [Fact]
    public void Departed_colleagues_leave_the_employee_reach_of_others()
    {
        _world.People[0] = _world.People[0] with { ExitDate = new DateOnly(2026, 6, 30) };

        For(Max, July).Privileges["absence:approve"].EmployeeIds.ShouldBe([Max]);
    }

    [Fact]
    public void Store_change_in_bc_moves_the_reach()
    {
        _world.People[1] = _world.People[1] with { HomeStoreId = F019 };

        var max = For(Max, July);

        max.Privileges["absence:approve"].StoreIds.ShouldBe([F019]);
        max.Privileges["absence:approve"].EmployeeIds.ShouldBe([Max]);
    }

    [Fact]
    public void Overview_lists_all_reached_stores_and_employees_including_oneself()
    {
        var max = For(Max, July);

        max.StoreIds.ShouldBe([F017]);
        max.EmployeeIds.ShouldBe([Erika, Max], ignoreOrder: true);
        For(Erika, July).EmployeeIds.ShouldBe([Erika]);
    }

    [Fact]
    public void Unknown_person_has_no_permissions()
    {
        For(Guid.NewGuid(), July).Privileges.ShouldBeEmpty();
    }
}
