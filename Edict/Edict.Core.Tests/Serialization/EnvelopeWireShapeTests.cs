using Edict.Contracts.Events;

using MessagePack;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// ADR 0007 schema-drift guard for EdictEventEnvelope (ADR 0024). Two
// snapshots — one per branch — fix the MessagePack key set the publisher
// and receiver will rely on. A renamed or removed property changes the
// snapshot and fails CI before the breaking wire change can ship.
public sealed class EnvelopeWireShapeTests
{
    static readonly Guid FixedEventId = new("44444444-4444-4444-4444-444444444444");
    static readonly DateTimeOffset FixedOccurredAt = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
    const string FixedTraceId = "0af7651916cd43dd8448eb211c80319c";
    const string FixedSpanId = "b7ad6b7169203331";

    [Fact]
    public Task EdictEventEnvelope_ShouldHaveStableWireShape_OnInlineBranch()
    {
        var envelope = new EdictEventEnvelope(inlinePayload: [0x01, 0x02, 0x03], claimCheckKey: null)
        {
            EventId = FixedEventId,
            OccurredAt = FixedOccurredAt,
            TraceId = FixedTraceId,
            SpanId = FixedSpanId,
            TraceState = null,
        };

        var bytes = MessagePackSerializer.Serialize(envelope);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return Verify(json);
    }

    [Fact]
    public Task EdictEventEnvelope_ShouldHaveStableWireShape_OnPointerBranch()
    {
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "blob/abcdef")
        {
            EventId = FixedEventId,
            OccurredAt = FixedOccurredAt,
            TraceId = FixedTraceId,
            SpanId = FixedSpanId,
            TraceState = null,
        };

        var bytes = MessagePackSerializer.Serialize(envelope);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return Verify(json);
    }
}
