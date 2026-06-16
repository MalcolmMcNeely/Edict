using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Audit;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.Audit;

public sealed class AuditMessageDeserializerTests
{
    static readonly Serializer Serializer = BuildSerializer();

    static EdictAuditRecord Record(EdictAuditKind kind, Type messageType) => new()
    {
        RecordId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        Kind = kind,
        MessageType = messageType.FullName!,
    };

    [Fact]
    public void Deserialize_ShouldRecoverConcreteCommand_FromCommandBody()
    {
        var command = new PlaceOrderCommand(new Guid("11111111-1111-1111-1111-111111111111"), "SKU-1");
        var body = Serializer.SerializeToArray<EdictCommand>(command);

        var message = AuditMessageDeserializer.Deserialize(
            Serializer, body, Record(EdictAuditKind.Command, typeof(PlaceOrderCommand)));

        var recovered = Assert.IsType<PlaceOrderCommand>(message);
        Assert.Equal(command, recovered);
    }

    [Fact]
    public void Deserialize_ShouldRecoverConcreteEvent_FromEventBody()
    {
        var raisedEvent = new OrderPlacedEvent(new Guid("22222222-2222-2222-2222-222222222222"), "SKU-2");
        var body = Serializer.SerializeToArray<EdictEvent>(raisedEvent);

        var message = AuditMessageDeserializer.Deserialize(
            Serializer, body, Record(EdictAuditKind.Event, typeof(OrderPlacedEvent)));

        var recovered = Assert.IsType<OrderPlacedEvent>(message);
        Assert.Equal(raisedEvent, recovered);
    }

    [Fact]
    public void Deserialize_ShouldThrowCarryingRecordIdAndMessageType_WhenBodyCannotBeDeserialized()
    {
        var record = Record(EdictAuditKind.Command, typeof(PlaceOrderCommand));

        var exception = Assert.Throws<EdictAuditMessageDeserializationException>(() =>
            AuditMessageDeserializer.Deserialize(Serializer, [0x00, 0xFF, 0x01, 0x02], record));

        Assert.Equal(record.RecordId, exception.RecordId);
        Assert.Equal(record.MessageType, exception.MessageType);
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(AuditMessageDeserializerTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
