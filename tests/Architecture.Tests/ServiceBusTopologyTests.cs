using System.Text.Json;
using System.Text.RegularExpressions;
using Shared.Messaging;

namespace Architecture.Tests;

/// <summary>
/// One topology, two descriptions (plan, phase 7): <c>infra/servicebus.bicep</c> (Azure) and <c>infra/servicebus-emulator.json</c>
/// (local emulator) must agree, cover the message catalogue and every subscription the services register.
/// </summary>
public sealed partial class ServiceBusTopologyTests
{
    private static string InfraFile(string name) => Path.Combine(Solution.Root, "infra", name);

    private sealed record Topology(SortedSet<string> Topics, SortedSet<string> Queues, SortedSet<string> Subscriptions, SortedSet<string> Filters);

    private static Topology FromEmulator()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(InfraFile("servicebus-emulator.json")));
        var ns = document.RootElement.GetProperty("UserConfig").GetProperty("Namespaces")[0];
        var topology = new Topology([], [], [], []);
        foreach (var queue in ns.GetProperty("Queues").EnumerateArray())
        {
            topology.Queues.Add(queue.GetProperty("Name").GetString()!);
        }

        foreach (var topic in ns.GetProperty("Topics").EnumerateArray())
        {
            var topicName = topic.GetProperty("Name").GetString()!;
            topology.Topics.Add(topicName);
            foreach (var sub in topic.GetProperty("Subscriptions").EnumerateArray())
            {
                var subName = sub.GetProperty("Name").GetString()!;
                topology.Subscriptions.Add($"{topicName}/{subName}");
                if (sub.TryGetProperty("Rules", out var rules))
                {
                    foreach (var rule in rules.EnumerateArray())
                    {
                        topology.Filters.Add($"{topicName}/{subName}: {rule.GetProperty("Properties").GetProperty("SqlFilter").GetProperty("SqlExpression").GetString()}");
                    }
                }
            }
        }

        return topology;
    }

    private static Topology FromBicep()
    {
        var text = File.ReadAllText(InfraFile("servicebus.bicep"));
        var topology = new Topology([], [], [], []);
        var topicSymbols = new Dictionary<string, string>();
        var subSymbols = new Dictionary<string, string>();
        foreach (Match m in TopicRegex().Matches(text))
        {
            topicSymbols[m.Groups[1].Value] = m.Groups[2].Value;
            topology.Topics.Add(m.Groups[2].Value);
        }

        foreach (Match m in QueueRegex().Matches(text))
        {
            topology.Queues.Add(m.Groups[1].Value);
        }

        foreach (Match m in SubscriptionRegex().Matches(text))
        {
            var key = $"{topicSymbols[m.Groups[2].Value]}/{m.Groups[3].Value}";
            subSymbols[m.Groups[1].Value] = key;
            topology.Subscriptions.Add(key);
        }

        foreach (Match m in RuleRegex().Matches(text))
        {
            topology.Filters.Add($"{subSymbols[m.Groups[1].Value]}: {m.Groups[2].Value.Replace("\\'", "'", StringComparison.Ordinal)}");
        }

        return topology;
    }

    [GeneratedRegex(@"resource (topic_\w+) 'Microsoft\.ServiceBus/namespaces/topics@[^']+' = \{\s+parent: sbNamespace\s+name: '([^']+)'")]
    private static partial Regex TopicRegex();

    [GeneratedRegex(@"resource queue_\w+ 'Microsoft\.ServiceBus/namespaces/queues@[^']+' = \{\s+parent: sbNamespace\s+name: '([^']+)'")]
    private static partial Regex QueueRegex();

    [GeneratedRegex(@"resource (sub_\w+) 'Microsoft\.ServiceBus/namespaces/topics/subscriptions@[^']+' = \{\s+parent: (topic_\w+)\s+name: '([^']+)'")]
    private static partial Regex SubscriptionRegex();

    [GeneratedRegex(@"subscriptions/rules@[^']+' = \{\s+parent: (sub_\w+)\s+name: '[^']+'\s+properties: \{\s+filterType: 'SqlFilter'\s+sqlFilter: \{\s+sqlExpression: '((?:[^'\\]|\\.)+)'")]
    private static partial Regex RuleRegex();

    [Fact]
    public void Bicep_and_emulator_describe_the_same_topology()
    {
        var bicep = FromBicep();
        var emulator = FromEmulator();

        bicep.Topics.ShouldBe(emulator.Topics);
        bicep.Queues.ShouldBe(emulator.Queues);
        bicep.Subscriptions.ShouldBe(emulator.Subscriptions);
        bicep.Filters.ShouldBe(emulator.Filters);
        emulator.Subscriptions.ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_catalogue_message_has_its_topic_or_queue()
    {
        var topology = FromBicep();
        foreach (var message in MessageRulesTests.MessageTypes(Solution.All("Contracts")))
        {
            var name = MessagingNames.For(message);
            (typeof(IEvent).IsAssignableFrom(message) ? topology.Topics : topology.Queues)
                .ShouldContain(name, $"{message.Name} needs a destination in the topology");
        }
    }

    [Fact]
    public void Every_subscription_a_service_registers_exists_in_the_topology()
    {
        var topology = FromBicep();
        var registered = new SortedSet<string>();
        foreach (var (path, lines) in Solution.SourceFiles().Where(f => f.RelativePath.EndsWith("InfrastructureServiceCollectionExtensions.cs", StringComparison.Ordinal)))
        {
            var text = string.Join('\n', lines);
            var service = path.Split(Path.DirectorySeparatorChar)[0].ToLowerInvariant();
            foreach (Match m in ExplicitSubscription().Matches(text))
            {
                registered.Add($"{MessagingNames.FromTypeName(m.Groups[1].Value)}/{m.Groups[2].Value}");
            }

            foreach (Match m in GenericSubscription().Matches(text))
            {
                registered.Add($"{MessagingNames.FromTypeName(m.Groups[1].Value)}/{service}");
            }
        }

        registered.ShouldNotBeEmpty();
        foreach (var subscription in registered)
        {
            topology.Subscriptions.ShouldContain(subscription, $"{subscription} is registered by a service but missing in infra/servicebus.bicep");
        }

        // The other way round: no subscription without a consumer (it would collect messages nobody reads).
        topology.Subscriptions.Except(registered).ShouldBeEmpty("subscriptions without a registered handler");
    }

    [GeneratedRegex(@"MessageSource\.Subscription\(MessagingNames\.For\(typeof\((?!TMessage)(\w+)\)\),\s*""(\w+)""\)")]
    private static partial Regex ExplicitSubscription();

    [GeneratedRegex(@"Subscription<(?!TMessage)(\w+)>\(\)")]
    private static partial Regex GenericSubscription();
}
