using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Tests.TestSupport;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.DeadLetter;

public sealed class DeadLetterPromotionTests
{
    static readonly Guid FixedEntryId = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid FixedOrderId = new("22222222-2222-2222-2222-222222222222");
    static readonly Guid FixedSourceEventId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    static readonly DateTimeOffset FixedDeadLetteredAt =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    const string SourceGrainKey = "33333333-3333-3333-3333-333333333333";
    const string SourceGrainType = "Sample.OrderCommandHandler";
    const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    static readonly StubEdictEventStreamAccessors Accessors = new();

    static OutboxEntry PublishEventEntry(string? traceParent = TraceParent) => new()
    {
        EntryId = FixedEntryId,
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [],
        TraceParent = traceParent,
        TraceState = null,
        AttemptCount = 3,
    };

    [Fact]
    public void Build_ShouldPopulateEffectTarget_WhenPublishEvent()
    {
        var entry = PublishEventEntry();
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal("Orders/OrderPlacedEvent", raised.EffectTarget);
    }

    [Fact]
    public void Build_ShouldCarryOriginatingCorrelation_WhenPublishEvent()
    {
        var correlationId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var entry = PublishEventEntry();
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1") { CorrelationId = correlationId };

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(correlationId, raised.CorrelationId);
    }

    [Fact]
    public void Build_ShouldCarryOriginatingCorrelation_WhenSendCommand()
    {
        var correlationId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var entry = new OutboxEntry
        {
            EntryId = FixedEntryId,
            Kind = OutboxEffectKind.SendCommand,
            Payload = [],
            AttemptCount = 3,
        };
        var command = new PlaceOrderCommand(FixedOrderId, "ITEM-1") { CorrelationId = correlationId };

        var raised = DeadLetterPromotion.Build(
            entry, command, "Sample.PaymentCommandHandler", FixedOrderId.ToString("N"),
            new InvalidOperationException("rejected"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(correlationId, raised.CorrelationId);
    }

    [Fact]
    public void Build_ShouldPopulateEffectTarget_WhenSendCommand()
    {
        var entry = new OutboxEntry
        {
            EntryId = FixedEntryId,
            Kind = OutboxEffectKind.SendCommand,
            Payload = [],
            AttemptCount = 3,
        };
        var command = new PlaceOrderCommand(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, command, "Sample.PaymentCommandHandler", FixedOrderId.ToString("N"),
            new InvalidOperationException("rejected"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(
            $"Sample.PaymentCommandHandler/{FixedOrderId:N}",
            raised.EffectTarget);
        // A SendCommand effect has no source Event, so SourceEventId stays null.
        Assert.Null(raised.SourceEventId);
    }

    [Fact]
    public void Build_ShouldPopulateEffectTarget_WhenUpsertRow()
    {
        var entry = new OutboxEntry
        {
            EntryId = FixedEntryId,
            Kind = OutboxEffectKind.UpsertRow,
            Payload = [],
            AttemptCount = 3,
        };
        var effect = new UpsertRowEffect
        {
            TableName = "OrderSummary",
            PartitionKey = "orders",
            RowKey = FixedOrderId.ToString("N"),
            RowAlias = "OrderSummaryRow",
            RowBytes = [0x01, 0x02, 0x03],
        };

        var raised = DeadLetterPromotion.Build(
            entry, effect, payloadJson: null, new InvalidOperationException("boom"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(
            $"OrderSummary/orders/{FixedOrderId:N}",
            raised.EffectTarget);
        // An UpsertRow effect has no source Event, so SourceEventId stays null.
        Assert.Null(raised.SourceEventId);
    }

    [Fact]
    public void Build_ShouldSerialisePayloadAsJson_WhenPublishEvent()
    {
        var entry = PublishEventEntry();
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.NotNull(raised.PayloadJson);
        Assert.Contains("\"OrderId\"", raised.PayloadJson);
        Assert.Contains(FixedOrderId.ToString(), raised.PayloadJson);
        Assert.Contains("\"Sku\"", raised.PayloadJson);
        Assert.Contains("ITEM-1", raised.PayloadJson);
    }

    [Fact]
    public void Build_ShouldPropagateTraceParent_WhenPresentOnOutboxEntry()
    {
        var entry = PublishEventEntry(traceParent: TraceParent);
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(TraceParent, raised.TraceParent);
    }

    [Fact]
    public void Build_ShouldOmitTraceParent_WhenAbsentOnOutboxEntry()
    {
        var entry = PublishEventEntry(traceParent: null);
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Null(raised.TraceParent);
    }

    [Fact]
    public void Build_ShouldCaptureExceptionTypeAndMessage_WhenExceptionProvided()
    {
        var entry = PublishEventEntry();
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("downstream unavailable"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal("System.InvalidOperationException", raised.ExceptionType);
        Assert.Equal("downstream unavailable", raised.Reason);
    }

    [Fact]
    public void Build_ShouldPreserveOriginalEntryId_AsDeadLetterEntryId()
    {
        var entry = PublishEventEntry();
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(FixedEntryId, raised.EntryId);
    }

    [Fact]
    public Task Build_ShouldProduceFullyPopulatedRaisedEvent_WhenPublishEvent()
    {
        var entry = PublishEventEntry();
        var edictEvent = new OrderPlacedEvent(FixedOrderId, "ITEM-1") { EventId = FixedSourceEventId };

        var raised = DeadLetterPromotion.Build(
            entry, edictEvent, Accessors, new InvalidOperationException("downstream unavailable"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        // The forensic row carries the source event's own id so operators can
        // trace a publish-failure row back to the event that produced it.
        Assert.Equal(FixedSourceEventId, raised.SourceEventId);
        return Verify(raised).DontScrubGuids().DontScrubDateTimes();
    }

    // A pointer-bearing envelope must recover the source event id onto the
    // dead-letter event and leave PayloadJson null — the >32 KB body never tries
    // to fit into the Azure Table property.
    [Fact]
    public Task BuildForEnvelopeFailure_ShouldCarrySourceEventIdAndOmitPayloadJson()
    {
        var entry = PublishEventEntry();
        var envelope = new EdictEventEnvelope(
            inlinePayload: null,
            eventId: FixedSourceEventId)
        {
            InnerEventStreamName = "Orders",
            InnerEventRouteKey = FixedOrderId.ToString("N"),
        };

        var raised = DeadLetterPromotion.BuildForEnvelopeFailure(
            entry, envelope, new InvalidOperationException("downstream unavailable"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        // The envelope mirrors the inner event's id, so the forensic row recovers
        // the source event id even though the oversized body never inlines here.
        Assert.Equal(FixedSourceEventId, raised.SourceEventId);
        return Verify(raised).DontScrubGuids().DontScrubDateTimes();
    }

    // The receiver-side BlobMissing row carries the BlobMissing failure-kind and
    // recovers the source event id from the envelope — that id is the claim-check
    // locator for the body it pointed at, even though the body is gone.
    [Fact]
    public Task BuildForBlobMissing_ShouldCarrySourceEventId()
    {
        var entry = PublishEventEntry();
        var envelope = new EdictEventEnvelope(
            inlinePayload: null,
            eventId: FixedSourceEventId)
        {
            InnerEventStreamName = "Orders",
            InnerEventRouteKey = FixedOrderId.ToString("N"),
        };

        var raised = DeadLetterPromotion.BuildForBlobMissing(
            entry, envelope, new KeyNotFoundException("blob not found"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(FixedSourceEventId, raised.SourceEventId);
        return Verify(raised).DontScrubGuids().DontScrubDateTimes();
    }
}
