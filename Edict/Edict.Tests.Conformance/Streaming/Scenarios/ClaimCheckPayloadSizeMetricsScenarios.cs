using System.Diagnostics.Metrics;

using Edict.Telemetry;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance that the <c>edict.claim_check.payload.size</c>
/// instrument records the inner-event byte length on a payload-spilled raise.
/// The recording is synchronous with the publish, so it is a streaming-publish
/// property: by the time <c>Send</c> returns the histogram has already received
/// the spilled-event observation.
/// </summary>
public abstract class ClaimCheckPayloadSizeMetricsScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected ClaimCheckPayloadSizeMetricsScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PayloadSizeMetric_ShouldFire_OnAPayloadSpilledRaise()
    {
        var counterId = Guid.NewGuid();
        var payload = new string('x', 64);

        var captures = new List<long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.ClaimCheck.Meters.PayloadSize)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == SemanticConventions.Events.Tags.ClaimChecked && (bool?)t.Value == true)
                {
                    lock (captures) { captures.Add(value); }
                    return;
                }
            }
        });
        listener.Start();

        await _fixture.Sender.SendAsync(new IncrementClaimCheckCounterCommand(counterId, payload));

        long capturedValue;
        lock (captures)
        {
            Assert.NotEmpty(captures);
            capturedValue = captures[0];
        }
        Assert.True(capturedValue > 0, "spilled payload size must be > 0");
    }
}
