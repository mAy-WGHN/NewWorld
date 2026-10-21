using Shared.Infrastructure.Messaging;
using Shared.Messaging;

namespace Shared.Infrastructure.Tests;

public class ServiceBusMessageMapperTests
{
    [Fact]
    public void Maps_ids_type_and_body_without_losing_correlation()
    {
        var outbound = new OutboundMessage(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            "EmployeeChanged",
            MessageKind.Event,
            "employee-changed",
            """{"x":1}""",
            new DateTimeOffset(2028, 2, 29, 23, 59, 59, TimeSpan.Zero));

        var message = ServiceBusMessageMapper.ToServiceBusMessage(outbound);

        message.MessageId.ShouldBe("00000000-0000-0000-0000-000000000001");
        message.CorrelationId.ShouldBe("00000000-0000-0000-0000-000000000002");
        message.Subject.ShouldBe("EmployeeChanged");
        message.ContentType.ShouldBe("application/json");
        message.Body.ToString().ShouldBe("""{"x":1}""");
        message.ApplicationProperties["OccurredAt"].ShouldBe(outbound.OccurredAt);
    }

    [Fact]
    public void Message_id_is_set_for_service_bus_duplicate_detection()
    {
        var id = Guid.NewGuid();
        var outbound = new OutboundMessage(id, Guid.NewGuid(), "T", MessageKind.Command, "t", "{}", DateTimeOffset.UnixEpoch);

        ServiceBusMessageMapper.ToServiceBusMessage(outbound).MessageId.ShouldBe(id.ToString());
    }
}
