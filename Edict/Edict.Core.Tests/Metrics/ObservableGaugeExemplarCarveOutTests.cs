using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Core.Metrics;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.Time.Testing;

namespace Edict.Core.Tests.Metrics;

// The three observable gauges are scraped on demand by the collector, never inside
// a grain turn, so no operation is in flight when they observe — an exemplar is
// impossible by construction. Lock that the gauge callbacks run with no recording
// Edict span current even while the trace-based exemplar wiring is active.
[Collection(EdictListenerUnitCollection.Name)]
public sealed class ObservableGaugeExemplarCarveOutTests
{
    static readonly DateTimeOffset Now = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SemanticConventions.Outbox.Meters.PendingCount)]
    [InlineData(SemanticConventions.Outbox.Meters.OldestEntryAge)]
    [InlineData(SemanticConventions.Sagas.Meters.ProgressAge)]
    public void Gauge_ShouldObserve_WithoutASpanInFlight_ByDesign(string instrumentName)
    {
        var marker = $"ObservableGaugeCarveOutTest_{Guid.NewGuid():N}";
        var cache = new EdictMetricsCache(new FakeTimeProvider(Now));
        cache.ReportOutbox(marker, "grain-1", pendingCount: 3, oldestEnqueuedAt: Now.AddSeconds(-5));
        cache.ReportSaga(marker, "saga-1", lastHandledAt: Now.AddSeconds(-5));

        using var activityListener = StartRecordingEdictListener();

        var sawObservation = false;
        Activity? currentAtObserve = null;
        using var meterListener = StartListener(instrumentName, marker, current =>
        {
            currentAtObserve = current;
            sawObservation = true;
        });

        meterListener.RecordObservableInstruments();

        Assert.True(sawObservation);
        Assert.Null(currentAtObserve);
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

    static MeterListener StartListener(string instrumentName, string grainTypeMarker, Action<Activity?> onObservation)
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
        listener.SetMeasurementEventCallback<int>((instrument, _, tags, _) =>
            ObserveIfMarked(instrument.Name, instrumentName, tags, grainTypeMarker, onObservation));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            ObserveIfMarked(instrument.Name, instrumentName, tags, grainTypeMarker, onObservation));
        listener.Start();
        return listener;
    }

    static void ObserveIfMarked(
        string actualName,
        string instrumentName,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string grainTypeMarker,
        Action<Activity?> onObservation)
    {
        if (actualName != instrumentName)
        {
            return;
        }
        foreach (var tag in tags)
        {
            if (tag.Key == SemanticConventions.Common.Tags.GrainType && (tag.Value as string) == grainTypeMarker)
            {
                onObservation(Activity.Current);
                return;
            }
        }
    }
}
