using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.Commands;
using Edict.Core.Commands;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

namespace Edict.Core.Tests.Commands;

// command.handle.duration only carries an exemplar when the silo edict.command.handle
// span is current at Record time. RunAndRecordAsync records in a finally that runs
// after the awaited handler, so the surrounding handle span must still be current
// for the TraceBased filter to attach the slow-command trace.
[Collection(EdictListenerUnitCollection.Name)]
public sealed class CommandHandleDurationExemplarTests
{
    [Fact]
    public async Task RunAndRecordAsync_ShouldRecordDuration_WhileRecordingCommandHandleSpanIsCurrent()
    {
        var marker = $"CommandHandleDurationExemplarTest_{Guid.NewGuid():N}";
        using var activityListener = StartRecordingEdictListener();

        Activity? currentAtRecord = null;
        using var meterListener = StartListener(marker, current => currentAtRecord = current);

        using (EdictDiagnostics.ActivitySource.StartEdictCommandHandle(nameof(PlaceOrderCommand), default))
        {
            await CommandHandleMetrics.RunAndRecordAsync<PlaceOrderCommand>(
                async () =>
                {
                    await Task.Delay(5);
                    return new EdictCommandResult.Accepted();
                },
                grainTypeName: marker);
        }

        Assert.NotNull(currentAtRecord);
        Assert.Equal(
            $"{SemanticConventions.Commands.Spans.Handle} {nameof(PlaceOrderCommand)}",
            currentAtRecord!.OperationName);
        Assert.True(currentAtRecord.Recorded);
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

    static MeterListener StartListener(string grainTypeMarker, Action<Activity?> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enabler) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Commands.Meters.HandleDuration)
                {
                    enabler.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == SemanticConventions.Common.Tags.GrainType
                    && (tag.Value as string) == grainTypeMarker)
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
