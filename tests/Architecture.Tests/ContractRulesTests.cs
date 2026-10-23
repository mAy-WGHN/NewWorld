using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Shared.Messaging;

namespace Architecture.Tests;

/// <summary>Rules for messages (phase 2): the catalogue, privacy classes on the bus, shape and documentation of contracts.</summary>
public class ContractRulesTests
{
    /// <summary>
    /// The message catalogue (CLAUDE.md, "Nachrichtenkatalog"). New messages only after consultation: extending this list
    /// is the conscious step. Class changes happen in the message's [Sensitivity] attribute and here.
    /// </summary>
    private static readonly (string Publisher, string Message, bool IsEvent, SensitivityClass Class)[] Catalogue =
    [
        ("BcIntegration", "EmployeeChanged", true, SensitivityClass.OrganizationData),
        ("BcIntegration", "EmployeeHired", true, SensitivityClass.OrganizationData),
        ("BcIntegration", "EmployeeLeft", true, SensitivityClass.OrganizationData),
        ("BcIntegration", "EmployeeDataErasureRequested", true, SensitivityClass.OrganizationData),
        ("BcIntegration", "StoreChanged", true, SensitivityClass.OrganizationData),
        ("BcIntegration", "BcSyncSucceeded", true, SensitivityClass.OrganizationData),
        ("BcIntegration", "BcSyncFailed", true, SensitivityClass.OrganizationData),
        ("Access", "PermissionsChanged", true, SensitivityClass.OrganizationData),
        ("Access", "ResponsibilitiesChanged", true, SensitivityClass.OrganizationData), // decision 13
        ("Workforce", "EmployeeAvailabilityChanged", true, SensitivityClass.SensitiveNeutralized),
        ("Workforce", "ShiftUncovered", true, SensitivityClass.SensitiveNeutralized),
        ("Workforce", "SyncAbsenceToBc", false, SensitivityClass.SensitiveFacts),
        // Many publishers (all business APIs); owned by the receiver. Class 2 data allowed for InApp only.
        ("Notifications", "SendNotification", false, SensitivityClass.SensitiveFacts),
    ];

    private static readonly HashSet<Type> AllowedScalars =
    [
        typeof(string), typeof(bool), typeof(int), typeof(long), typeof(decimal), typeof(Guid),
        typeof(DateOnly), typeof(DateTimeOffset), typeof(TimeOnly),
    ];

    private static IEnumerable<Type> Messages() => MessageRulesTests.MessageTypes(Solution.All("Contracts"));

    [Fact]
    public void Contracts_contain_exactly_the_catalogue()
    {
        var actual = Messages()
            .Select(t => (Publisher: t.Assembly.GetName().Name!.Replace(".Contracts", "", StringComparison.Ordinal), Message: t.Name, IsEvent: typeof(IEvent).IsAssignableFrom(t), Class: SensitivityReader.Of(t)))
            .OrderBy(m => m.Message, StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(Catalogue.OrderBy(m => m.Message, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Every_message_is_either_an_event_or_a_command()
    {
        Messages().Where(t => typeof(IEvent).IsAssignableFrom(t) == typeof(ICommand).IsAssignableFrom(t))
            .Select(t => t.Name).ShouldBeEmpty();
    }

    [Fact]
    public void Events_on_topics_carry_class_2_only_neutralised()
    {
        Messages()
            .Where(t => typeof(IEvent).IsAssignableFrom(t))
            .Where(t => SensitivityReader.Of(t) is not (SensitivityClass.OrganizationData or SensitivityClass.SensitiveNeutralized))
            .Select(t => t.Name)
            .ShouldBeEmpty("events may only carry class 1 or neutralised class 2 content");
    }

    [Fact]
    public void Messages_and_their_records_are_immutable_sealed_records_of_allowed_types()
    {
        var violations = new List<string>();
        foreach (var message in Messages())
        {
            CheckRecord(message, message.Name, violations, []);
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Enums_in_contracts_have_unknown_zero_and_serialize_as_strings()
    {
        var violations = Solution.All("Contracts").SelectMany(a => a.GetTypes()).Where(t => t.IsEnum)
            .SelectMany(e =>
            {
                var problems = new List<string>();
                if (!Enum.IsDefined(e, 0) || Enum.GetName(e, 0) != "Unknown")
                {
                    problems.Add($"{e.Name}: Unknown = 0 missing");
                }

                if (e.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType is not { IsGenericType: true } converter
                    || converter.GetGenericTypeDefinition() != typeof(JsonStringEnumConverter<>))
                {
                    problems.Add($"{e.Name}: [JsonConverter(typeof(JsonStringEnumConverter<{e.Name}>))] missing");
                }

                return problems;
            })
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Every_message_documents_purpose_publisher_and_consumers()
    {
        var violations = new List<string>();
        foreach (var assembly in Solution.All("Contracts"))
        {
            var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
            File.Exists(xmlPath).ShouldBeTrue($"documentation file missing: {xmlPath}");
            var members = XDocument.Load(xmlPath).Descendants("member").ToDictionary(m => (string)m.Attribute("name")!);

            foreach (var message in MessageRulesTests.MessageTypes([assembly]))
            {
                if (!members.TryGetValue($"T:{message.FullName}", out var doc))
                {
                    violations.Add($"{message.Name}: no XML documentation");
                    continue;
                }

                var summary = doc.Element("summary")?.Value.Trim() ?? "";
                var remarks = doc.Element("remarks")?.Value ?? "";
                if (summary.Length == 0 || !remarks.Contains("Publisher:", StringComparison.Ordinal) || !remarks.Contains("Consumers:", StringComparison.Ordinal))
                {
                    violations.Add($"{message.Name}: needs <summary> (purpose) and <remarks> with 'Publisher:' and 'Consumers:'");
                }
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Every_message_has_a_verified_snapshot_in_its_publishers_tests()
    {
        var root = Directory.GetParent(Solution.SrcPath)!.FullName;
        Messages()
            .Select(t => Path.Combine(root, "tests", t.Assembly.GetName().Name + ".Tests", "Snapshots", t.Name + ".verified.json"))
            .Where(path => !File.Exists(path))
            .ShouldBeEmpty();
    }

    private static void CheckRecord(Type type, string path, List<string> violations, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        if (!type.IsSealed || type.GetMethod("<Clone>$") is null)
        {
            violations.Add($"{path}: must be a sealed record");
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is { } setter && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
            {
                violations.Add($"{path}.{property.Name}: must be init-only");
            }

            CheckType(property.PropertyType, $"{path}.{property.Name}", type.Assembly, violations, visited);
        }
    }

    private static void CheckType(Type type, string path, Assembly contracts, List<string> violations, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (AllowedScalars.Contains(type) || (type.IsEnum && type.Assembly == contracts))
        {
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            CheckType(type.GetGenericArguments()[0], path + "[]", contracts, violations, visited);
            return;
        }

        if (type == typeof(IReadOnlyDictionary<string, string>))
        {
            return;
        }

        if (type.IsClass && type.Assembly == contracts)
        {
            CheckRecord(type, path, violations, visited);
            return;
        }

        violations.Add($"{path}: type {type.Name} is not allowed in contracts");
    }
}
