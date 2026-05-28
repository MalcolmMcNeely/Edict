using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.ClaimCheck;

public sealed class ClaimCheckPolicyTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ApplyAsync_ShouldReturnInnerEventBytesAndSkipStore_WhenUnderThreshold()
    {
        var store = new RecordingStore();
        var evt = new OrderPlacedEvent(Guid.NewGuid(), "SKU-SMALL");
        var expected = Serializer.SerializeToArray<EdictEvent>(evt);
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 30_720, store, new StubEdictEventStreamAccessors());

        var result = await policy.ApplyAsync(evt, CancellationToken.None);

        Assert.Equal(expected, result.Payload);
        Assert.Same(evt, result.WireEvent);
        Assert.Empty(store.Puts);
    }

    [Fact]
    public async Task ApplyAsync_ShouldPutBytesAndReturnPointerEnvelope_WhenOverThreshold()
    {
        var store = new RecordingStore();
        // SKU large enough that the serialized event crosses the threshold by a
        // healthy margin, so the size_bytes tag has a deterministic ballpark.
        var evt = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256));
        var innerBytes = Serializer.SerializeToArray<EdictEvent>(evt);
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());

        var result = await policy.ApplyAsync(evt, CancellationToken.None);

        Assert.Single(store.Puts);
        Assert.Equal(innerBytes, store.Puts[0].Payload.ToArray());
        var envelope = Serializer.Deserialize<EdictEvent>(result.Payload);
        var pointer = Assert.IsType<EdictEventEnvelope>(envelope);
        Assert.Null(pointer.InlinePayload);
        Assert.Equal(store.Puts[0].ReturnedKey, pointer.ClaimCheckKey);
        var wirePointer = Assert.IsType<EdictEventEnvelope>(result.WireEvent);
        Assert.Equal(store.Puts[0].ReturnedKey, wirePointer.ClaimCheckKey);
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrowEnvelopeOverflow_WhenWrappedBytesExceedMaxEnvelopeBytes()
    {
        // Stub store returns an extremely long key so the serialized envelope
        // crosses the 32 KB framing cap even though the inner event is small.
        var store = new FixedKeyStore(new string('K', 40_000));
        var routeKey = Guid.NewGuid();
        var evt = new OrderPlacedEvent(routeKey, "SKU-A");
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 1, store, new StubEdictEventStreamAccessors());

        var ex = await Assert.ThrowsAsync<EdictEnvelopeOverflowException>(
            () => policy.ApplyAsync(evt, CancellationToken.None));

        Assert.Equal(routeKey, ex.RouteKey);
        Assert.Equal(typeof(OrderPlacedEvent).FullName, ex.EventType);
        Assert.True(ex.MeasuredBytes > 32_768);
    }

    [Fact]
    public async Task ApplyAsync_ShouldEmitClaimCheckPutSpanAndTagParent_WhenPathFires()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var store = new RecordingStore();
        var evt = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256));
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());

        using (var parent = EdictDiagnostics.ActivitySource.StartActivity($"{SemanticConventions.Events.Spans.Publish} OrderPlacedEvent"))
        {
            Assert.NotNull(parent);
            await policy.ApplyAsync(evt, CancellationToken.None);
            Assert.Equal(true, parent.GetTagItem(SemanticConventions.Events.Tags.ClaimChecked));
        }

        var put = stopped.Single(a => a.OperationName == SemanticConventions.ClaimCheck.Spans.Put);
        Assert.Equal(nameof(OrderPlacedEvent), put.GetTagItem(SemanticConventions.Events.Tags.Type));
        Assert.NotNull(put.GetTagItem(SemanticConventions.Events.Tags.SizeBytes));
        Assert.Equal(store.Puts[0].ReturnedKey, put.GetTagItem(SemanticConventions.ClaimCheck.Tags.Key));
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrow_WhenStoreNotConfiguredButThresholdExceeded()
    {
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 1, store: null, accessors: new StubEdictEventStreamAccessors());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => policy.ApplyAsync(new OrderPlacedEvent(Guid.NewGuid(), "SKU"), CancellationToken.None));
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(ClaimCheckPolicyTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed record PutCall(ReadOnlyMemory<byte> Payload, string ReturnedKey);

    sealed class RecordingStore : IEdictClaimCheckStore
    {
        public List<PutCall> Puts { get; } = [];

        public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
            var key = $"key-{Puts.Count + 1:D4}";
            Puts.Add(new PutCall(payload, key));
            return Task.FromResult(key);
        }

        public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException("publisher-side tests never fetch");
    }

    sealed class FixedKeyStore(string key) : IEdictClaimCheckStore
    {
        public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken ct) =>
            Task.FromResult(key);

        public Task<ReadOnlyMemory<byte>> GetAsync(string k, CancellationToken ct) =>
            throw new NotSupportedException("publisher-side tests never fetch");
    }
}
