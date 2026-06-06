using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Edict.Telemetry;

namespace Edict.Core.Tests.Idempotency;

public sealed class IdempotencyDuplicateCounterTests
{
    [Theory]
    [InlineData(SemanticConventions.Idempotency.Tags.ReasonValues.Window)]
    [InlineData(SemanticConventions.Idempotency.Tags.ReasonValues.InFlight)]
    public void EmitDedupHit_ShouldIncrementCounter_TaggedWithEventTypeGrainTypeAndReason(string reason)
    {
        var marker = $"IdempotencyDuplicateCounterTest_{Guid.NewGuid():N}";
        var captures = new List<Capture>();
        using var listener = StartListener(captures, marker);

        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), "SKU")
        {
            EventId = Guid.NewGuid(),
        };

        IdempotencyDedupMetrics.EmitDedupHit(edictEvent, grainTypeName: marker, reason);

        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(nameof(OrderPlacedEvent), capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(marker, capture.Tag(SemanticConventions.Common.Tags.GrainType));
        Assert.Equal(reason, capture.Tag(SemanticConventions.Idempotency.Tags.Reason));
    }

    static MeterListener StartListener(List<Capture> captures, string grainTypeMarker)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.Idempotency.Meters.DuplicateCount)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags) { dict[t.Key] = t.Value; }
            if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeMarker)
            {
                captures.Add(new Capture(value, dict));
            }
        });
        listener.Start();
        return listener;
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }
}
