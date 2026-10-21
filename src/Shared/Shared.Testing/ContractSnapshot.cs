using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiffEngine;
using Shared.Messaging;
using VerifyTests;
using VerifyXunit;

namespace Shared.Testing;

/// <summary>
/// Snapshot of a message's wire format (<see cref="MessageJson"/>). The publisher verifies a sample instance;
/// consumers read the verified file as test input, so both sides test against the same JSON.
/// Files: <c>tests/&lt;Service&gt;.Contracts.Tests/Snapshots/&lt;Message&gt;.verified.json</c>.
/// </summary>
public static class ContractSnapshot
{
    private const string Directory = "Snapshots";

    private static readonly JsonSerializerOptions Indented = new(MessageJson.Options) { WriteIndented = true };

    static ContractSnapshot()
    {
        // Never open a diff tool; a mismatch fails the test and leaves a .received.json next to the snapshot.
        DiffRunner.Disabled = true;
    }

    /// <summary>Verifies the JSON of <paramref name="sample"/>. Call from a test in the publisher's Contracts.Tests root folder.</summary>
    public static SettingsTask Verify<T>(T sample, [CallerFilePath] string sourceFile = "") where T : IMessage =>
        Verifier.Verify(JsonSerializer.Serialize(sample, Indented), "json", sourceFile: sourceFile)
            .UseDirectory(Directory)
            .UseFileName(typeof(T).Name);

    /// <summary>Verified JSON of a message, as published by <paramref name="publisherService"/> (e.g. <c>BcIntegration</c>).</summary>
    public static string ReadJson(string publisherService, string messageName)
    {
        var path = Path.Combine(RepositoryPaths.Root, "tests", $"{publisherService}.Contracts.Tests", Directory, $"{messageName}.verified.json");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"No verified snapshot for {messageName} of {publisherService}.", path);
    }

    /// <summary>The publisher's sample message, deserialized as a consumer would.</summary>
    public static T Load<T>(string publisherService) => MessageJson.Deserialize<T>(ReadJson(publisherService, typeof(T).Name));

    /// <summary>
    /// Forward compatibility as a consumer: the publisher's snapshot plus unknown fields (top level and in every nested
    /// record) deserializes without error and without losing any known value. Dictionaries are open maps and get no extra key.
    /// </summary>
    public static void AssertToleratesUnknownFields<T>(string publisherService)
    {
        var original = JsonNode.Parse(ReadJson(publisherService, typeof(T).Name))!;
        var extended = original.DeepClone();
        AddUnknownFields(extended, typeof(T));

        var message = MessageJson.Deserialize<T>(extended.ToJsonString());
        var roundTripped = JsonNode.Parse(MessageJson.Serialize(message!))!;

        if (!JsonNode.DeepEquals(original, roundTripped))
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} lost or changed values when unknown fields were added.{Environment.NewLine}" +
                $"Expected: {original.ToJsonString()}{Environment.NewLine}Actual:   {roundTripped.ToJsonString()}");
        }
    }

    private static void AddUnknownFields(JsonNode? node, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        switch (node)
        {
            case JsonObject obj when !IsDictionary(type):
                foreach (var property in type.GetProperties())
                {
                    var name = MessageJson.Options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
                    if (obj[name] is { } child)
                    {
                        AddUnknownFields(child, property.PropertyType);
                    }
                }

                obj["fieldAddedInAFutureVersion"] = new JsonObject { ["nested"] = new JsonArray(1, "two") };
                break;
            case JsonArray array when ElementType(type) is { } elementType:
                foreach (var item in array)
                {
                    AddUnknownFields(item, elementType);
                }

                break;
        }
    }

    private static bool IsDictionary(Type type) =>
        type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    private static Type? ElementType(Type type) =>
        type.GetInterfaces().Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
}
