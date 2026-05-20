using Edict.Contracts.DeadLetter;

using MessagePack;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// ADR 0007 schema-drift guard for the new dead-letter contracts (ADR 0022).
// Each test serialises a fully-populated instance to MessagePack bytes,
// converts to JSON, and snapshots the result. A renamed or removed property
// changes the string key in the snapshot and fails CI before the breaking
// wire change can ship silently. Inputs are fixed constants so snapshots are
// deterministic.

public sealed class DeadLetterContractWireShapeTests
{
    static readonly Guid FixedEntryId = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    static readonly DateTimeOffset FixedDeadLetteredAt =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task EdictDeadLetterEntry_ShouldHaveStableWireShape()
    {
        var entry = new EdictDeadLetterEntry
        {
            EntryId = FixedEntryId,
            Kind = "PublishEvent",
            AttemptCount = 3,
            DeadLetteredAt = FixedDeadLetteredAt,
            SourceGrainKey = "11111111-1111-1111-1111-111111111111",
            SourceGrainType = "Sample.OrderCommandHandler",
            EffectTarget = "orders/OrderPlacedEvent",
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            ExceptionType = "System.InvalidOperationException",
            Reason = "downstream unavailable",
            PayloadJson = "{\"OrderId\":\"22222222-2222-2222-2222-222222222222\",\"Sku\":\"ITEM-1\"}",
        };

        var bytes = MessagePackSerializer.Serialize(entry);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return Verify(json);
    }

    [Fact]
    public Task EdictDeadLetterRaised_ShouldHaveStableWireShape()
    {
        var raised = new EdictDeadLetterRaised
        {
            EntryId = FixedEntryId,
            Kind = "SendCommand",
            AttemptCount = 5,
            DeadLetteredAt = FixedDeadLetteredAt,
            SourceGrainKey = "11111111-1111-1111-1111-111111111111",
            SourceGrainType = "Sample.OrderCommandHandler",
            EffectTarget = "Sample.PaymentCommandHandler/33333333-3333-3333-3333-333333333333",
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            ExceptionType = "System.InvalidOperationException",
            Reason = "command rejected",
            PayloadJson = "{\"PaymentId\":\"33333333-3333-3333-3333-333333333333\",\"Amount\":42}",
            EventId = new Guid("44444444-4444-4444-4444-444444444444"),
            OccurredAt = new DateTimeOffset(2026, 5, 19, 12, 0, 1, TimeSpan.Zero),
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            SpanId = "b7ad6b7169203331",
            TraceState = null,
        };

        var bytes = MessagePackSerializer.Serialize(raised);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return Verify(json);
    }

    [Fact]
    public void EdictDeadLetterRaised_ShouldRouteToSingletonGrainKey()
    {
        var raised = new EdictDeadLetterRaised();

        Assert.Equal(EdictDeadLetterRaised.SingletonGrainKey, raised.SingletonKey);
    }
}
