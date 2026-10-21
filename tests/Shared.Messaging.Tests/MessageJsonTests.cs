using System.Text.Json.Serialization;
using Shared.Messaging;

namespace Shared.Messaging.Tests;

public class MessageJsonTests
{
    [JsonConverter(typeof(JsonStringEnumConverter<Colour>))]
    public enum Colour
    {
        Unknown = 0,
        Red = 1,
    }

    public sealed record Sample(Guid MessageId, Guid CorrelationId, DateTimeOffset OccurredAt, Colour Colour, DateOnly Day, string? Note);

    private static readonly Sample Value = new(
        Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Guid.Parse("00000000-0000-0000-0000-000000000002"),
        new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)),
        Colour.Red,
        new DateOnly(2028, 2, 29),
        null);

    [Fact]
    public void Writes_camel_case_iso_dates_and_enums_as_strings()
    {
        MessageJson.Serialize(Value).ShouldBe(
            """{"messageId":"00000000-0000-0000-0000-000000000001","correlationId":"00000000-0000-0000-0000-000000000002","occurredAt":"2026-10-25T02:30:00+02:00","colour":"Red","day":"2028-02-29","note":null}""");
    }

    [Fact]
    public void Keeps_umlauts_readable_but_escapes_html_sensitive_characters()
    {
        var json = MessageJson.Serialize(Value with { Note = "Straße <b>" });

        json.ShouldContain("\"note\":\"Straße \\u003Cb\\u003E\"");
    }

    [Fact]
    public void Round_trips()
    {
        MessageJson.Deserialize<Sample>(MessageJson.Serialize(Value)).ShouldBe(Value);
    }

    [Fact]
    public void Ignores_unknown_fields_for_forward_compatibility()
    {
        var json = MessageJson.Serialize(Value).Replace("\"note\":null", "\"note\":null,\"addedLater\":{\"x\":[1,2]}", StringComparison.Ordinal);

        MessageJson.Deserialize<Sample>(json).ShouldBe(Value);
    }

    [Fact]
    public void Unknown_enum_text_fails_instead_of_being_guessed()
    {
        var json = MessageJson.Serialize(Value).Replace("\"Red\"", "\"Purple\"", StringComparison.Ordinal);

        Should.Throw<System.Text.Json.JsonException>(() => MessageJson.Deserialize<Sample>(json));
    }
}
