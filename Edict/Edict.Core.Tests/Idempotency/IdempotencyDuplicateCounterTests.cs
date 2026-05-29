using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Edict.Telemetry;

namespace Edict.Core.Tests.Idempotency;

public sealed class IdempotencyDuplicateCounterTests
{
    [Fact]
    public void EmitDedupHit_ShouldIncrementCounter_TaggedWithEventTypeAndGrainType()
    {
        var marker = $"IdempotencyDuplicateCounterTest_{Guid.NewGuid():N}";
        var captures = new List<Capture>();
        using var listener = StartListener(captures, marker);

        var evt = new OrderPlacedEvent(Guid.NewGuid(), "SKU")
        {
            EventId = Guid.NewGuid(),
        };

        IdempotencyDedupMetrics.EmitDedupHit(evt, grainTypeName: marker);

        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(nameof(OrderPlacedEvent), capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(marker, capture.Tag(SemanticConventions.Common.Tags.GrainType));
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
