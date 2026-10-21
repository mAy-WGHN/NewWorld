using System.Reflection;
using Shared.Messaging;

namespace Architecture.Tests;

public class MessageRulesTests
{
    /// <summary>All message types (implementing <see cref="IMessage"/>) of the given assemblies.</summary>
    internal static IEnumerable<Type> MessageTypes(IEnumerable<Assembly> assemblies) =>
        assemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IMessage).IsAssignableFrom(t));

    internal static IReadOnlyList<string> Violations(IEnumerable<Type> messages)
    {
        var result = new List<string>();
        foreach (var type in messages)
        {
            switch (SensitivityReader.Of(type))
            {
                case SensitivityClass.Unknown:
                    result.Add($"{type.FullName}: missing [Sensitivity] (or class Unknown)");
                    break;
                case SensitivityClass.HighlySensitive:
                    result.Add($"{type.FullName}: class 3 must never be on the bus");
                    break;
            }
        }

        return result;
    }

    [Sensitivity(SensitivityClass.OrganizationData)]
    private sealed record Good(Guid MessageId, Guid CorrelationId, DateTimeOffset OccurredAt) : IEvent;

    private sealed record Unclassified(Guid MessageId, Guid CorrelationId, DateTimeOffset OccurredAt) : IEvent;

    [Sensitivity(SensitivityClass.HighlySensitive)]
    private sealed record Salary(Guid MessageId, Guid CorrelationId, DateTimeOffset OccurredAt) : IEvent;

    [Fact]
    public void Rule_accepts_classified_messages() => Violations([typeof(Good)]).ShouldBeEmpty();

    [Fact]
    public void Rule_rejects_unclassified_messages() => Violations([typeof(Unclassified)]).ShouldHaveSingleItem();

    [Fact]
    public void Rule_rejects_class_3_messages() => Violations([typeof(Salary)]).ShouldHaveSingleItem();

    [Fact]
    public void All_messages_in_contracts_are_classified_and_none_is_class_3() =>
        Violations(MessageTypes(Solution.All("Contracts"))).ShouldBeEmpty();

    [Fact]
    public void Messages_are_declared_in_contracts_only()
    {
        var misplaced = MessageTypes(Solution.AllServiceAssemblies().Where(a => !a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal)))
            .Select(t => t.FullName);

        misplaced.ShouldBeEmpty("messages belong into <Service>.Contracts");
    }
}
