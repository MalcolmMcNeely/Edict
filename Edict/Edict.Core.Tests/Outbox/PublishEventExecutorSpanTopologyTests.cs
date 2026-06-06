using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.Outbox;

// Pins the per-turn causality invariant (ADR-0060) on the publish span: the
// inline drain that still holds the live event reference nests the publish under
// the staging command; a recovery drain (no live ref) makes the publish its own
// root that links back to the staging command. The signal is per-entry, so a
// mixed batch parents one and links the other.
[Collection(TestSupport.EdictListenerUnitCollection.Name)]
public sealed class PublishEventExecutorSpanTopologyTests
{
    static readonly Serializer Serializer = BuildSerializer();

    static readonly Guid OrderId = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid PayloadEventId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    const string StagingTraceIdHex = "0af7651916cd43dd8448eb211c80319c";
    const string StagingSpanIdHex = "b7ad6b7169203331";
    const string StagingTraceParent = $"00-{StagingTraceIdHex}-{StagingSpanIdHex}-01";

    static readonly ActivityTraceId StagingTraceId = ActivityTraceId.CreateFromString(StagingTraceIdHex);
    static readonly ActivitySpanId StagingSpanId = ActivitySpanId.CreateFromString(StagingSpanIdHex);

    [Fact]
    public async Task ExecuteAsync_ShouldNestPublishUnderStaging_WhenLiveReferencePresent()
    {
        var stopped = ListenForPublishSpans();

        var edictEvent = new OrderPlacedEvent(OrderId, "SKU-1") { EventId = PayloadEventId };
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();

        // Live ref present: the publish runs in the same turn that raised the event.
        await executor.ExecuteAsync(
            PublishEntry(edictEvent), stream, deferredDispatch: null, consumerType: null, liveWireEvent: edictEvent);

        var publish = SinglePublishSpan(stopped);
        Assert.Equal(StagingTraceId, publish.TraceId);
        Assert.Equal(StagingSpanId, publish.ParentSpanId);
        Assert.Empty(publish.Links);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRootAndLinkToStaging_WhenLiveReferenceAbsent()
    {
        var stopped = ListenForPublishSpans();

        var edictEvent = new OrderPlacedEvent(OrderId, "SKU-1") { EventId = PayloadEventId };
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();

        // No live ref: a reminder / activation drain rehydrated the entry in a later turn.
        await executor.ExecuteAsync(
            PublishEntry(edictEvent), stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);

        var publish = SinglePublishSpan(stopped);
        Assert.Equal(default, publish.ParentSpanId);
        Assert.NotEqual(StagingTraceId, publish.TraceId);

        var onlyLink = Assert.Single(publish.Links);
        Assert.Equal(StagingTraceId, onlyLink.Context.TraceId);
        Assert.Equal(StagingSpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBeBareRoot_WhenRecoveryDrainHasNoCapturedTrace()
    {
        var stopped = ListenForPublishSpans();

        var edictEvent = new OrderPlacedEvent(OrderId, "SKU-1") { EventId = PayloadEventId };
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();

        // Recovery drain of an entry whose staging command ran with no trace.
        await executor.ExecuteAsync(
            PublishEntryWithoutTrace(edictEvent), stream, deferredDispatch: null, consumerType: null, liveWireEvent: null);

        var publish = SinglePublishSpan(stopped);
        Assert.Equal(default, publish.ParentSpanId);
        Assert.Empty(publish.Links);

        // The stamped event carries the bare root's real ids — never a synthesised
        // all-zero trace id, which a consumer's ActivityTraceId.CreateFromString rejects.
        var published = Assert.Single(stream.Captured);
        Assert.NotEqual(new string('0', 32), published.TraceId);
    }

    [Fact]
    public async Task ExecuteBatchAsync_ShouldParentLiveEntryAndLinkRecoveredEntry()
    {
        var stopped = ListenForPublishSpans();

        var liveEvent = new OrderPlacedEvent(OrderId, "SKU-live") { EventId = PayloadEventId };
        var recoveredEvent = new OrderPlacedEvent(OrderId, "SKU-recovered")
        {
            EventId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        };
        var executor = BuildExecutor();
        var stream = new CapturingStreamProvider();

        // One inline-raised entry (live ref) riding alongside one leftover pending
        // entry rehydrated from durable state (null ref) in the same drain pass.
        await executor.ExecuteBatchAsync(
            [PublishEntry(liveEvent), PublishEntry(recoveredEvent)],
            stream,
            deferredDispatch: null,
            consumerType: null,
            liveWireEvents: [liveEvent, null]);

        Assert.Equal(2, stopped.Count);

        var child = Assert.Single(stopped, span => span.ParentSpanId != default);
        Assert.Equal(StagingTraceId, child.TraceId);
        Assert.Equal(StagingSpanId, child.ParentSpanId);
        Assert.Empty(child.Links);

        var root = Assert.Single(stopped, span => span.ParentSpanId == default);
        Assert.NotEqual(StagingTraceId, root.TraceId);
        var onlyLink = Assert.Single(root.Links);
        Assert.Equal(StagingTraceId, onlyLink.Context.TraceId);
        Assert.Equal(StagingSpanId, onlyLink.Context.SpanId);
    }

    static List<Activity> ListenForPublishSpans()
    {
        var stopped = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} {nameof(OrderPlacedEvent)}")
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return stopped;
    }

    static Activity SinglePublishSpan(List<Activity> stopped) => Assert.Single(stopped);

    static OutboxEntry PublishEntry(OrderPlacedEvent edictEvent) => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = OutboxEffectKind.PublishEvent,
        Payload = Serializer.SerializeToArray<EdictEvent>(edictEvent),
        TraceParent = StagingTraceParent,
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
            builder.AddAssembly(typeof(PublishEventExecutorSpanTopologyTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
