using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Outbox;
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
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), "SKU-SMALL");
        var expected = Serializer.SerializeToArray<EdictEvent>(edictEvent);
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 30_720, store, new StubEdictEventStreamAccessors());

        var result = await policy.ApplyAsync(edictEvent, CancellationToken.None);

        Assert.Equal(expected, result.Payload);
        Assert.Same(edictEvent, result.WireEvent);
        Assert.Empty(store.Puts);
    }

    [Fact]
    public async Task ApplyAsync_ShouldPutBytesAndReturnPointerEnvelope_WhenOverThreshold()
    {
        var store = new RecordingStore();
        // SKU large enough that the serialized event crosses the threshold by a
        // healthy margin, so the size_bytes tag has a deterministic ballpark.
        // EventId is already stamped by enqueue time, before the policy runs.
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256)) { EventId = Guid.NewGuid() };
        var innerBytes = Serializer.SerializeToArray<EdictEvent>(edictEvent);
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());

        var result = await policy.ApplyAsync(edictEvent, CancellationToken.None);

        Assert.Single(store.Puts);
        Assert.Equal(innerBytes, store.Puts[0].Payload.ToArray());
        // The store is keyed by the event's own id, and the pointer envelope
        // carries that same id as its identity — there is no separate key.
        Assert.Equal(edictEvent.EventId, store.Puts[0].EventId);
        var envelope = Serializer.Deserialize<EdictEvent>(result.Payload);
        var pointer = Assert.IsType<EdictEventEnvelope>(envelope);
        Assert.Null(pointer.InlinePayload);
        Assert.Equal(edictEvent.EventId, pointer.EventId);
        var wirePointer = Assert.IsType<EdictEventEnvelope>(result.WireEvent);
        Assert.Equal(edictEvent.EventId, wirePointer.EventId);
    }

    [Fact]
    public async Task ApplyAsync_ShouldMirrorInnerEventIdOntoPointerEnvelope_WhenOverThreshold()
    {
        var store = new RecordingStore();
        // The id the event already carries by enqueue time, before the policy runs.
        var stampedId = Guid.NewGuid();
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256)) { EventId = stampedId };
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());

        var result = await policy.ApplyAsync(edictEvent, CancellationToken.None);

        // The receiver dedups on the envelope id before fetching the blob, so it
        // must equal the inner event's stamped id — otherwise every oversized
        // event dedups against Guid.Empty and the second is wrongly suppressed.
        var envelope = Assert.IsType<EdictEventEnvelope>(Serializer.Deserialize<EdictEvent>(result.Payload));
        Assert.Equal(stampedId, envelope.EventId);
        var storedInner = Serializer.Deserialize<EdictEvent>(store.Puts[0].Payload.ToArray());
        Assert.Equal(stampedId, storedInner.EventId);
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrowEnvelopeOverflow_WhenWrappedBytesExceedMaxEnvelopeBytes()
    {
        var store = new RecordingStore();
        var routeKey = Guid.NewGuid();
        var edictEvent = new OrderPlacedEvent(routeKey, "SKU-A") { EventId = Guid.NewGuid() };
        // A pathologically long inner-stream name inflates the framing envelope
        // past the 32 KB Azure Table property cap even though the parked body is
        // tiny — the pointer envelope no longer carries a key to overflow.
        var accessors = new HugeStreamNameAccessors(new string('S', 40_000), routeKey);
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 1, store, accessors);

        var exception = await Assert.ThrowsAsync<EdictEnvelopeOverflowException>(
            () => policy.ApplyAsync(edictEvent, CancellationToken.None));

        Assert.Equal(routeKey, exception.RouteKey);
        Assert.Equal(typeof(OrderPlacedEvent).FullName, exception.EventType);
        Assert.True(exception.MeasuredBytes > 32_768);
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
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256)) { EventId = Guid.NewGuid() };
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());

        using (var parent = EdictDiagnostics.ActivitySource.StartActivity($"{SemanticConventions.Events.Spans.Publish} OrderPlacedEvent"))
        {
            Assert.NotNull(parent);
            await policy.ApplyAsync(edictEvent, CancellationToken.None);
            Assert.Equal(true, parent.GetTagItem(SemanticConventions.Events.Tags.ClaimChecked));
        }

        var put = stopped.Single(a => a.OperationName == SemanticConventions.ClaimCheck.Spans.Put);
        Assert.Equal(nameof(OrderPlacedEvent), put.GetTagItem(SemanticConventions.Events.Tags.Type));
        Assert.NotNull(put.GetTagItem(SemanticConventions.Events.Tags.SizeBytes));
        Assert.Equal(edictEvent.EventId.ToString(), put.GetTagItem(SemanticConventions.ClaimCheck.Tags.Key));
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

    sealed record PutCall(Guid EventId, ReadOnlyMemory<byte> Payload);

    sealed class RecordingStore : IEdictClaimCheckStore
    {
        public List<PutCall> Puts { get; } = [];

        public Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Puts.Add(new PutCall(eventId, payload));
            return Task.CompletedTask;
        }

        public Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("publisher-side tests never fetch");
    }

    sealed class HugeStreamNameAccessors(string streamName, Guid routeKey) : IEventStreamAccessors
    {
        public (string StreamName, Guid RouteKey) Resolve(EdictEvent edictEvent) => (streamName, routeKey);
    }
}
