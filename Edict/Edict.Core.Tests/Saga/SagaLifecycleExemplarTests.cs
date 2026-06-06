using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Core.Sagas;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

namespace Edict.Core.Tests.Saga;

// saga.timeout.fired is recorded inside the edict.saga.timeout span (the fired-cap
// turn wraps its durable commit, the same way the production cap reminder does), so
// it carries an exemplar. saga.completed is counted post-durable-commit for
// exactly-once, where no span is in flight, so it is a deliberate carve-out: a
// rare write-fault redelivery must not double-count it, and the inline completion
// path has no handle span at the count point.
[Collection(EdictListenerUnitCollection.Name)]
public sealed class SagaLifecycleExemplarTests
{
    [Fact]
    public void EmitTimeoutFired_ShouldRecordCounter_WhileRecordingSagaTimeoutSpanIsCurrent()
    {
        var sagaType = $"SagaLifecycleExemplarTest_{Guid.NewGuid():N}";
        using var activityListener = StartRecordingEdictListener();

        Activity? currentAtRecord = null;
        using var meterListener = StartListener(
            SemanticConventions.Sagas.Meters.TimeoutFired, sagaType, current => currentAtRecord = current);

        using (EdictDiagnostics.ActivitySource.StartEdictSagaTimeout(sagaType, link: null))
        {
            SagaLifecycleMetrics.EmitTimeoutFired(
                sagaType, SemanticConventions.Sagas.Tags.OutcomeValues.Deadlettered);
        }

        Assert.NotNull(currentAtRecord);
        Assert.Equal($"{SemanticConventions.Sagas.Spans.Timeout} {sagaType}", currentAtRecord!.OperationName);
        Assert.True(currentAtRecord.Recorded);
    }

    [Fact]
    public void EmitCompleted_ShouldRecordCounter_WithoutASpanInFlight_ByDesign()
    {
        var sagaType = $"SagaLifecycleExemplarTest_{Guid.NewGuid():N}";
        using var activityListener = StartRecordingEdictListener();

        var sawMeasurement = false;
        Activity? currentAtRecord = null;
        using var meterListener = StartListener(
            SemanticConventions.Sagas.Meters.Completed, sagaType, current =>
            {
                currentAtRecord = current;
                sawMeasurement = true;
            });

        SagaLifecycleMetrics.EmitCompleted(sagaType);

        Assert.True(sawMeasurement);
        Assert.Null(currentAtRecord);
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

    static MeterListener StartListener(string instrumentName, string sagaTypeMarker, Action<Activity?> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enabler) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName && instrument.Name == instrumentName)
                {
                    enabler.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name != instrumentName)
            {
                return;
            }
            foreach (var tag in tags)
            {
                if (tag.Key == SemanticConventions.Common.Tags.GrainType
                    && (tag.Value as string) == sagaTypeMarker)
                {
                    onMeasurement(Activity.Current);
                    return;
                }
            }
        });
        listener.Start();
        return listener;
    }
}
