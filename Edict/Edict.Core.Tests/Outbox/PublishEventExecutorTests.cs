using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.Outbox;

[Collection(TestSupport.EdictListenerUnitCollection.Name)]
public sealed class PublishEventExecutorTests
{
    static readonly Serializer Serializer = BuildSerializer();

    static readonly Guid OrderId = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid PayloadEventId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public async Task ExecuteAsync_ShouldPublishPayloadEventId_WhenDrainedWithNoLiveRef()
    {
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();
        var entry = PublishEntry(new OrderPlacedEvent(OrderId, "SKU-1") { EventId = PayloadEventId });

        // No live ref — the crash-recovery drain rehydrates from the durable payload.
        await executor.ExecuteAsync(entry, stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);

        Assert.Equal(PayloadEventId, Assert.Single(stream.Captured).EventId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPublishSameEventId_WhenSameEntryDrainsTwiceWithNoLiveRef()
    {
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();
        var entry = PublishEntry(new OrderPlacedEvent(OrderId, "SKU-1") { EventId = PayloadEventId });

        // The producer re-publish: a fault in the publish-succeeded /
        // ack-not-persisted window re-drains the same Pending entry.
        await executor.ExecuteAsync(entry, stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);
        await executor.ExecuteAsync(entry, stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);

        Assert.Equal(2, stream.Captured.Count);
        Assert.Equal(PayloadEventId, stream.Captured[0].EventId);
        Assert.Equal(stream.Captured[0].EventId, stream.Captured[1].EventId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepEventIdStableButVarySpanId_AcrossTwoDrains()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();
        var entry = PublishEntry(new OrderPlacedEvent(OrderId, "SKU-1") { EventId = PayloadEventId });

        await executor.ExecuteAsync(entry, stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);
        await executor.ExecuteAsync(entry, stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);

        // One stable event identity; each delivery attempt is its own publish span.
        Assert.Equal(stream.Captured[0].EventId, stream.Captured[1].EventId);
        Assert.NotEqual(stream.Captured[0].SpanId, stream.Captured[1].SpanId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStampNullTraceIds_WhenNoCapturedTraceParentAndNoAmbientActivity()
    {
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();
        // Captured traceparent is null — the command ran with no trace — and no
        // ActivityListener is registered here, so the publish opens no activity.
        var entry = PublishEntryWithoutTrace(new OrderPlacedEvent(OrderId, "SKU-1") { EventId = PayloadEventId });

        await executor.ExecuteAsync(entry, stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);

        var published = Assert.Single(stream.Captured);
        Assert.Null(published.TraceId);
        Assert.Null(published.SpanId);
        // Never a synthesised all-zero id, which a consumer's ActivityTraceId.CreateFromString rejects.
        Assert.NotEqual(new string('0', 32), published.TraceId);
        Assert.NotEqual(new string('0', 16), published.SpanId);
    }

    [Fact]
    public async Task ExecuteBatchAsync_ShouldPublishEachPayloadEventId()
    {
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();
        var firstId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var secondId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var entries = new[]
        {
            PublishEntry(new OrderPlacedEvent(OrderId, "SKU-1") { EventId = firstId }),
            PublishEntry(new OrderPlacedEvent(OrderId, "SKU-2") { EventId = secondId }),
        };

        await executor.ExecuteBatchAsync(
            entries, stream, deferredDispatch: null, consumerType: null, liveWireEvents: [null, null]);

        var publishedIds = stream.Captured.Select(captured => captured.EventId).ToArray();
        Assert.Equal([firstId, secondId], publishedIds);
    }

    static OutboxEntry PublishEntry(OrderPlacedEvent edictEvent) => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = OutboxEffectKind.PublishEvent,
        Payload = Serializer.SerializeToArray<EdictEvent>(edictEvent),
        TraceParent = TraceParent,
    };

    static OutboxEntry PublishEntryWithoutTrace(OrderPlacedEvent edictEvent) => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = OutboxEffectKind.PublishEvent,
        Payload = Serializer.SerializeToArray<EdictEvent>(edictEvent),
        TraceParent = null,
    };

    static PublishEventExecutor BuildExecutor() =>
        new(Serializer, new StubEdictEventStreamAccessors(),
            new EventTagWriters(new Dictionary<Type, Action<EdictEvent, Activity>>()));

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(PublishEventExecutorTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
