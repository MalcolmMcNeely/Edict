using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.DeadLetter;

public sealed class DeadLetterPromotionTests
{
    static readonly Guid FixedEntryId = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid FixedOrderId = new("22222222-2222-2222-2222-222222222222");
    static readonly DateTimeOffset FixedDeadLetteredAt =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    const string SourceGrainKey = "33333333-3333-3333-3333-333333333333";
    const string SourceGrainType = "Sample.OrderCommandHandler";
    const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

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
        var evt = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, evt, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal("Orders/OrderPlacedEvent", raised.EffectTarget);
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
            entry, command, "Sample.PaymentCommandHandler",
            new InvalidOperationException("rejected"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(
            $"Sample.PaymentCommandHandler/{FixedOrderId:D}",
            raised.EffectTarget);
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
            RowJson = "{\"OrderId\":\"22222222-2222-2222-2222-222222222222\"}"u8.ToArray(),
        };

        var raised = DeadLetterPromotion.Build(
            entry, effect, new InvalidOperationException("boom"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(
            $"OrderSummary/orders/{FixedOrderId:N}",
            raised.EffectTarget);
    }

    [Fact]
    public void Build_ShouldSerialisePayloadAsJson_WhenPublishEvent()
    {
        var entry = PublishEventEntry();
        var evt = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, evt, new InvalidOperationException("nope"),
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
        var evt = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, evt, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(TraceParent, raised.TraceParent);
    }

    [Fact]
    public void Build_ShouldOmitTraceParent_WhenAbsentOnOutboxEntry()
    {
        var entry = PublishEventEntry(traceParent: null);
        var evt = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, evt, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Null(raised.TraceParent);
    }

    [Fact]
    public void Build_ShouldCaptureExceptionTypeAndMessage_WhenExceptionProvided()
    {
        var entry = PublishEventEntry();
        var evt = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, evt, new InvalidOperationException("downstream unavailable"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal("System.InvalidOperationException", raised.ExceptionType);
        Assert.Equal("downstream unavailable", raised.Reason);
    }

    [Fact]
    public void Build_ShouldPreserveOriginalEntryId_AsDeadLetterEntryId()
    {
        var entry = PublishEventEntry();
        var evt = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, evt, new InvalidOperationException("nope"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        Assert.Equal(FixedEntryId, raised.EntryId);
    }

    [Fact]
    public Task Build_ShouldProduceFullyPopulatedRaisedEvent_WhenPublishEvent()
    {
        var entry = PublishEventEntry();
        var evt = new OrderPlacedEvent(FixedOrderId, "ITEM-1");

        var raised = DeadLetterPromotion.Build(
            entry, evt, new InvalidOperationException("downstream unavailable"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        return Verify(raised).DontScrubGuids().DontScrubDateTimes();
    }

    // A pointer-bearing envelope must lift ClaimCheckKey onto the dead-letter
    // event and leave PayloadJson null — the >32 KB body never tries to fit
    // into the Azure Table property.
    [Fact]
    public Task BuildForEnvelopeFailure_ShouldCarryClaimCheckKeyAndOmitPayloadJson()
    {
        var entry = PublishEventEntry();
        var envelope = new EdictEventEnvelope(
            inlinePayload: null,
            claimCheckKey: "edict-claim-check/abc123")
        {
            InnerEventStreamName = "Orders",
            InnerEventRouteKey = FixedOrderId,
        };

        var raised = DeadLetterPromotion.BuildForEnvelopeFailure(
            entry, envelope, new InvalidOperationException("downstream unavailable"),
            SourceGrainKey, SourceGrainType, FixedDeadLetteredAt);

        return Verify(raised).DontScrubGuids().DontScrubDateTimes();
    }
}
