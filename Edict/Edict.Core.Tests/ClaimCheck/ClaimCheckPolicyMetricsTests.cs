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

public sealed class ClaimCheckPolicyMetricsTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ApplyAsync_UnderThreshold_ShouldRecordPayloadSizeInBytes_TaggedWithEventTypeAndUnspilled()
    {
        // Use a SKU whose length is unique to this test (test name length + GUID
        // collision check is statistically impossible) so the captured byte size
        // is distinguishable from any other test's payload size — the
        // ClaimCheckPolicy doesn't emit a grain-type tag we could otherwise key on.
        var sku = "SKU-MetricsUnder-" + Guid.NewGuid().ToString("N");
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), sku);
        var expectedSize = Serializer.SerializeToArray<EdictEvent>(edictEvent).Length;

        var captures = new List<Capture>();
        using var listener = StartListener(captures, expectedSize);

        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 30_720, store: null, new StubEdictEventStreamAccessors());

        await policy.ApplyAsync(edictEvent, CancellationToken.None);

        var capture = Assert.Single(captures);
        Assert.Equal(expectedSize, capture.Value);
        Assert.Equal(nameof(OrderPlacedEvent), capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(false, capture.Tag(SemanticConventions.Events.Tags.ClaimChecked));
    }

    [Fact]
    public async Task ApplyAsync_OverThreshold_ShouldRecordInnerEventBytes_NotEnvelopeBytes_TaggedSpilled()
    {
        // Unique payload size per-test as above.
        var sku = "SKU-MetricsOver-" + Guid.NewGuid().ToString("N") + new string('x', 128);
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), sku);
        var innerSize = Serializer.SerializeToArray<EdictEvent>(edictEvent).Length;

        var captures = new List<Capture>();
        using var listener = StartListener(captures, innerSize);

        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, new InMemoryStore(), new StubEdictEventStreamAccessors());

        await policy.ApplyAsync(edictEvent, CancellationToken.None);

        var capture = Assert.Single(captures);
        Assert.Equal(innerSize, capture.Value);
        Assert.Equal(nameof(OrderPlacedEvent), capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(true, capture.Tag(SemanticConventions.Events.Tags.ClaimChecked));
    }

    static MeterListener StartListener(List<Capture> captures, long expectedValue)
    {
        var listener = new MeterListener
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
            if (value != expectedValue) { return; }
            var dict = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags) { dict[t.Key] = t.Value; }
            captures.Add(new Capture(value, dict));
        });
        listener.Start();
        return listener;
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(ClaimCheckPolicyMetricsTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }

    sealed class InMemoryStore : IEdictClaimCheckStore
    {
        public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            Task.FromResult($"k-{Guid.NewGuid():N}");

        public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
