using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

namespace Edict.Core.Tests.Idempotency;

// A duplicate-count measurement only carries an exemplar when a recording Edict
// span is current at Record time, so the TraceBased filter can attach the
// suppressed-redelivery trace. This locks that EmitDedupSpanAndHit records the
// counter while the edict.event.deduplicated span is in flight.
[Collection(EdictListenerUnitCollection.Name)]
public sealed class IdempotencyDuplicateExemplarTests
{
    [Fact]
    public void EmitDedupSpanAndHit_ShouldRecordCounter_WhileRecordingDeduplicatedSpanIsCurrent()
    {
        var marker = $"IdempotencyDuplicateExemplarTest_{Guid.NewGuid():N}";
        using var activityListener = StartRecordingEdictListener();

        Activity? currentAtRecord = null;
        var sawMeasurement = false;
        using var meterListener = StartListener(marker, (current, _) =>
        {
            currentAtRecord = current;
            sawMeasurement = true;
        });

        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), "SKU")
        {
            EventId = Guid.NewGuid(),
        };

        IdempotencyDedupMetrics.EmitDedupSpanAndHit(edictEvent, grainTypeName: marker);

        Assert.True(sawMeasurement);
        Assert.NotNull(currentAtRecord);
        Assert.Equal(
            $"{SemanticConventions.Events.Spans.Deduplicated} {nameof(OrderPlacedEvent)}",
            currentAtRecord!.OperationName);
        Assert.True(currentAtRecord.Recorded);
    }

    [Fact]
    public void EmitDedupSpan_ShouldEmitSpanOnly_WithoutRecordingCounter()
    {
        var marker = $"IdempotencyDuplicateExemplarTest_{Guid.NewGuid():N}";
        using var activityListener = StartRecordingEdictListener();

        var sawMeasurement = false;
        using var meterListener = StartListener(marker, (_, _) => sawMeasurement = true);

        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), "SKU")
        {
            EventId = Guid.NewGuid(),
        };

        IdempotencyDedupMetrics.EmitDedupSpan(edictEvent);

        Assert.False(sawMeasurement);
    }

    static ActivityListener StartRecordingEdictListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    static MeterListener StartListener(string grainTypeMarker, Action<Activity?, long> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enabler) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Idempotency.Meters.DuplicateCount)
                {
                    enabler.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == SemanticConventions.Common.Tags.GrainType
                    && (tag.Value as string) == grainTypeMarker)
                {
                    onMeasurement(Activity.Current, value);
                    return;
                }
            }
        });
        listener.Start();
        return listener;
    }
}
