using Edict.Contracts.Events;
using Edict.Core.Tests;

using MessagePack;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

public sealed class EventWireShapeTests
{
    private static readonly Guid FixedEventId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FixedAggregateId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public Task OrderPlacedEvent_ShouldHaveStableWireShape()
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

    private static Task VerifyWireShape<T>(T evt) where T : EdictEvent
    {
        var bytes = MessagePackSerializer.Serialize(evt);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return Verify(json);
    }
}
