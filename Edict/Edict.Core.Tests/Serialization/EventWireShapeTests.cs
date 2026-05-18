using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// ADR 0007 schema-drift guard for Events. Mirrors CommandWireShapeTests: each
// test serialises a concrete event to MessagePack bytes, converts to JSON, and
// snapshots the result. A renamed or removed property changes the string key
// and fails CI before the breaking wire change can ship silently. Fixed inputs
// keep the snapshot deterministic.

[MessagePackObject(keyAsPropertyName: true)]
[Stream("Orders")]
public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : Event
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public sealed class EventWireShapeTests
{
    private static readonly Guid FixedEventId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FixedAggregateId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public Task OrderPlacedEvent_wire_shape_is_stable()
    {
        var evt = new OrderPlacedEvent(FixedAggregateId, "ITEM-1")
        {
            EventId = FixedEventId,
            OccurredAt = new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero),
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            SpanId = "b7ad6b7169203331",
            TraceState = null,
        };

        return VerifyWireShape(evt);
    }

    private static Task VerifyWireShape<T>(T evt) where T : Event
    {
        var bytes = MessagePackSerializer.Serialize(evt);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return Verify(json);
    }
}
