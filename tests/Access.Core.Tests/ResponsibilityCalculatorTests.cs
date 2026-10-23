using Access.Core.Responsibilities;
using static Access.Core.Tests.World;

namespace Access.Core.Tests;

public class ResponsibilityCalculatorTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateTimeOffset July = new(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly World _world = new();

    public ResponsibilityCalculatorTests()
    {
        _world.Responsibilities.Add(Responsibility.Create("absence.approval", Max, ResponsibilityTarget.ForStore(F017)));
        _world.Responsibilities.Add(Responsibility.Create("audit.instruction", Sabine, ResponsibilityTarget.ForScope(DistrictSouth)));
        _world.Responsibilities.Add(Responsibility.Create("absence.approval", Tom, ResponsibilityTarget.ForStore(F017),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(2))));
    }

    private IReadOnlyDictionary<string, IReadOnlyList<Guid>> For(Guid employeeId, DateTimeOffset at) =>
        ResponsibilityCalculator.Calculate(employeeId, _world.Data(), at, Berlin);

    [Fact]
    public void Employee_gets_the_responsible_persons_of_their_store_and_scope_per_topic()
    {
        var erika = For(Erika, July);

        erika["absence.approval"].ShouldBe([Max]);
        erika["audit.instruction"].ShouldBe([Sabine]);
    }

    [Fact]
    public void Nobody_is_responsible_for_themself()
    {
        var max = For(Max, July);

        max.ContainsKey("absence.approval").ShouldBeFalse();
        max["audit.instruction"].ShouldBe([Sabine]);
    }

    [Fact]
    public void Time_limited_responsibility_applies_only_in_its_period()
    {
        For(Erika, new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.FromHours(2)))["absence.approval"].ShouldBe([Max, Tom], ignoreOrder: true);
        For(Erika, new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(2)))["absence.approval"].ShouldBe([Max]);
    }

    [Fact]
    public void Person_specific_responsibility_adds_to_the_store_one()
    {
        _world.Responsibilities.Add(Responsibility.Create("absence.approval", Sabine, ResponsibilityTarget.ForEmployee(Erika)));

        For(Erika, July)["absence.approval"].ShouldBe([Max, Sabine], ignoreOrder: true);
    }

    [Fact]
    public void Departed_responsible_person_is_no_longer_listed()
    {
        _world.People[1] = _world.People[1] with { ExitDate = new DateOnly(2026, 6, 30) };

        For(Erika, July).ContainsKey("absence.approval").ShouldBeFalse();
    }

    [Fact]
    public void Store_change_in_bc_changes_the_responsible_persons()
    {
        _world.People[0] = _world.People[0] with { HomeStoreId = F019 };

        var erika = For(Erika, July);

        erika.ContainsKey("absence.approval").ShouldBeFalse();
        erika["audit.instruction"].ShouldBe([Sabine]);
    }

    [Theory]
    [InlineData("absence.approval", true)]
    [InlineData("audit.instruction", true)]
    [InlineData("absence", true)]
    [InlineData("Absence.Approval", false)]
    [InlineData("absence..approval", false)]
    [InlineData("", false)]
    public void Topic_keys_have_a_fixed_format(string topic, bool valid) => Responsibility.IsValidTopic(topic).ShouldBe(valid);
}
