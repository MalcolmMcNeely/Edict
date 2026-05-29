using System.Diagnostics.Metrics;

using Edict.Contracts.Commands;
using Edict.Core.Commands;
using Edict.Telemetry;

namespace Edict.Core.Tests.Commands;

public sealed class CommandHandleMetricsTests
{
    [Fact]
    public async Task RunAndRecordAsync_ShouldRecordDurationInSeconds_TaggedWithCommandTypeAndGrainType()
    {
        var marker = $"CommandHandleMetricsTest_{Guid.NewGuid():N}";
        var captures = new List<Capture>();
        using var listener = StartListener(captures, marker);

        var accepted = new EdictCommandResult.Accepted();
        var result = await CommandHandleMetrics.RunAndRecordAsync<PlaceOrderCommand>(
            async () => { await Task.Delay(5); return accepted; }, grainTypeName: marker);

        Assert.Same(accepted, result);
        var capture = Assert.Single(captures);
        Assert.True(capture.Value > 0);
        Assert.True(capture.Value < 60, "must be seconds");
        Assert.Equal(nameof(PlaceOrderCommand), capture.Tag(SemanticConventions.Commands.Tags.Type));
        Assert.Equal(marker, capture.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    [Fact]
    public async Task RunAndRecordAsync_ShouldRecordDuration_EvenWhenHandleThrows()
    {
        var marker = $"CommandHandleMetricsTest_{Guid.NewGuid():N}";
        var captures = new List<Capture>();
        using var listener = StartListener(captures, marker);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CommandHandleMetrics.RunAndRecordAsync<FailOrderCommand>(
                () => throw new InvalidOperationException("boom"), grainTypeName: marker));

        var capture = Assert.Single(captures);
        Assert.Equal(nameof(FailOrderCommand), capture.Tag(SemanticConventions.Commands.Tags.Type));
    }

    static MeterListener StartListener(List<Capture> captures, string grainTypeMarker)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.Commands.Meters.HandleDuration)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
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

    sealed record Capture(double Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }
}
