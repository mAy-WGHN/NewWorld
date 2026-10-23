using BcIntegration.Core.MasterData;

namespace BcIntegration.Core.Tests;

public class StoreChangeDetectorTests
{
    private static StoreState State(bool active = true, params OpeningHours[] hours) =>
        new("F017", "Filiale Musterstadt", new Address("Hauptstraße 1", "12345", "Musterstadt"),
            hours.Length > 0 ? hours : [new OpeningHours(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(20, 0))],
            active);

    [Fact]
    public void First_sight_is_a_change() => StoreChangeDetector.HasChanged(null, State()).ShouldBeTrue();

    [Fact]
    public void Equal_content_in_new_list_instances_is_no_change() => StoreChangeDetector.HasChanged(State(), State()).ShouldBeFalse();

    [Fact]
    public void Changed_opening_hours_are_a_change() =>
        StoreChangeDetector.HasChanged(State(), State(true, new OpeningHours(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(21, 0)))).ShouldBeTrue();

    [Fact]
    public void Deactivation_is_a_change() => StoreChangeDetector.HasChanged(State(), State(active: false)).ShouldBeTrue();
}
