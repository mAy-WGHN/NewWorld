using BcIntegration.Core.MasterData;

namespace BcIntegration.Core.Tests;

public class EmployeeChangeDetectorTests
{
    private static readonly Guid Store = Guid.Parse("0197a000-0000-7000-8000-000000000101");

    private static EmployeeState State(DateOnly? entry = null, DateOnly? exit = null, string name = "Erika Mustermann") =>
        new("10042", name, Store, "VERKAUF", "TEILZEIT", 30m, entry ?? new DateOnly(2024, 2, 29), exit);

    [Fact]
    public void First_sight_during_the_initial_load_reports_the_state_only()
    {
        var changes = EmployeeChangeDetector.Detect(previous: null, State(exit: new DateOnly(2020, 12, 31)), lifecycleEvents: false);

        changes.ShouldBe(new EmployeeChanges(Changed: true, Hired: false, Left: false));
    }

    [Fact]
    public void New_employee_after_the_initial_load_is_reported_as_hired()
    {
        var changes = EmployeeChangeDetector.Detect(previous: null, State(entry: new DateOnly(2028, 2, 29)), lifecycleEvents: true);

        changes.ShouldBe(new EmployeeChanges(Changed: true, Hired: true, Left: false));
    }

    [Fact]
    public void New_employee_who_already_has_an_exit_date_is_hired_and_left()
    {
        var changes = EmployeeChangeDetector.Detect(null, State(exit: new DateOnly(2026, 12, 31)), lifecycleEvents: true);

        changes.ShouldBe(new EmployeeChanges(Changed: true, Hired: true, Left: true));
    }

    [Fact]
    public void Unchanged_state_produces_nothing()
    {
        EmployeeChangeDetector.Detect(State(), State(), lifecycleEvents: true).ShouldBe(EmployeeChanges.None);
    }

    [Fact]
    public void Changed_field_produces_exactly_one_change()
    {
        EmployeeChangeDetector.Detect(State(), State(name: "Erika Musterfrau"), lifecycleEvents: true)
            .ShouldBe(new EmployeeChanges(Changed: true, Hired: false, Left: false));
    }

    [Fact]
    public void Exit_date_set_means_left()
    {
        EmployeeChangeDetector.Detect(State(), State(exit: new DateOnly(2026, 12, 31)), lifecycleEvents: true)
            .ShouldBe(new EmployeeChanges(Changed: true, Hired: false, Left: true));
    }

    [Fact]
    public void Corrected_exit_date_is_reported_again()
    {
        EmployeeChangeDetector.Detect(State(exit: new DateOnly(2026, 12, 31)), State(exit: new DateOnly(2027, 1, 31)), lifecycleEvents: true)
            .ShouldBe(new EmployeeChanges(Changed: true, Hired: false, Left: true));
    }

    [Fact]
    public void Removed_exit_date_is_only_a_change()
    {
        EmployeeChangeDetector.Detect(State(exit: new DateOnly(2026, 12, 31)), State(), lifecycleEvents: true)
            .ShouldBe(new EmployeeChanges(Changed: true, Hired: false, Left: false));
    }

    [Fact]
    public void Changed_entry_date_means_hired_again()
    {
        EmployeeChangeDetector.Detect(State(entry: new DateOnly(2026, 1, 1)), State(entry: new DateOnly(2026, 2, 1)), lifecycleEvents: true)
            .ShouldBe(new EmployeeChanges(Changed: true, Hired: true, Left: false));
    }

    [Fact]
    public void Lifecycle_changes_are_not_reported_during_the_initial_load_even_for_known_employees()
    {
        EmployeeChangeDetector.Detect(State(), State(exit: new DateOnly(2026, 12, 31)), lifecycleEvents: false)
            .ShouldBe(new EmployeeChanges(Changed: true, Hired: false, Left: false));
    }
}
