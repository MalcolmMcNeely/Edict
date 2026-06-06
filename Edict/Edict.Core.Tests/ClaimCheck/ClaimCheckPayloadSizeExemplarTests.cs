using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.ClaimCheck;

// A payload-size measurement only carries an exemplar when a recording Edict span
// is current at Record time. A spilled event records inside the claim-check PUT
// span (built from the producer context, so the exemplar points at the blob-write
// trace); an under-threshold event records inside the ambient producer turn span.
[Collection(EdictListenerUnitCollection.Name)]
public sealed class ClaimCheckPayloadSizeExemplarTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ApplyAsync_OverThreshold_ShouldRecordPayloadSize_WhilePutSpanIsCurrent()
    {
        var sku = "SKU-ExemplarOver-" + Guid.NewGuid().ToString("N") + new string('x', 128);
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), sku) { EventId = Guid.NewGuid() };
        var innerSize = Serializer.SerializeToArray<EdictEvent>(edictEvent).Length;

        using var activityListener = StartRecordingEdictListener();
        Activity? currentAtRecord = null;
        using var meterListener = StartListener(innerSize, current => currentAtRecord = current);

        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, new InMemoryStore(), new StubEdictEventStreamAccessors());

        await policy.ApplyAsync(edictEvent, default, CancellationToken.None);

        Assert.NotNull(currentAtRecord);
        Assert.Equal(SemanticConventions.ClaimCheck.Spans.Put, currentAtRecord!.OperationName);
        Assert.True(currentAtRecord.Recorded);
    }

    [Fact]
    public async Task ApplyAsync_UnderThreshold_ShouldRecordPayloadSize_WhileProducerTurnSpanIsCurrent()
    {
        var sku = "SKU-ExemplarUnder-" + Guid.NewGuid().ToString("N");
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), sku);
        var innerSize = Serializer.SerializeToArray<EdictEvent>(edictEvent).Length;

        using var activityListener = StartRecordingEdictListener();
        Activity? currentAtRecord = null;
        using var meterListener = StartListener(innerSize, current => currentAtRecord = current);

        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 30_720, store: null, new StubEdictEventStreamAccessors());

        // The producer turn span the inline raise records inside — the command- or
        // event-handle span that is Activity.Current when the policy runs.
        using var producerTurn = EdictDiagnostics.ActivitySource.StartActivity(SemanticConventions.Commands.Spans.Handle);
        await policy.ApplyAsync(edictEvent, default, CancellationToken.None);

        Assert.NotNull(currentAtRecord);
        Assert.Same(producerTurn, currentAtRecord);
        Assert.True(currentAtRecord!.Recorded);
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

    static MeterListener StartListener(long expectedValue, Action<Activity?> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enabler) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.ClaimCheck.Meters.PayloadSize)
                {
                    enabler.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            if (value == expectedValue)
            {
                onMeasurement(Activity.Current);
            }
        });
        listener.Start();
        return listener;
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(ClaimCheckPayloadSizeExemplarTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed class InMemoryStore : IEdictClaimCheckStore
    {
        public Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
