using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.EventHandler;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.EventHandler;

public sealed class InvokeHandlerExecutorTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ExecuteAsync_ShouldDispatchInnerEvent_WhenInlineEnvelopeEntry()
    {
        var edictEvent = new OrderPlacedEvent(
            OrderId: new Guid("11111111-1111-1111-1111-111111111111"),
            Sku: "WIDGET")
        {
            EventId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        };
        var inlineBytes = Serializer.SerializeToArray<EdictEvent>(edictEvent);
        var envelope = EnvelopeCodec.WrapInline(inlineBytes);
        var entry = new OutboxEntry
        {
            EntryId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };
        var dispatched = new List<EdictEvent>();

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store: null), NoWriters, TimeProvider.System);
        await executor.ExecuteAsync(
            entry, NullStreamProvider.Instance, e => { dispatched.Add(e); return Task.FromResult<OutboxEntry?>(null); }, consumerType: typeof(object), liveWireEvent: null);

        await Verify(dispatched).DontScrubGuids();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFetchAndDispatchInnerEvent_WhenPointerEnvelopeEntry()
    {
        var inner = new OrderPlacedEvent(
            OrderId: new Guid("22222222-2222-2222-2222-222222222222"),
            Sku: "OVERSIZED")
        {
            EventId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        };
        var pointerId = new Guid("eeeeeeee-1111-2222-3333-444444444444");
        var store = new InMemoryClaimCheckStore();
        store.Blobs[pointerId] = Serializer.SerializeToArray<EdictEvent>(inner);

        var envelope = EnvelopeCodec.WrapPointer(pointerId);
        var entry = new OutboxEntry
        {
            EntryId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };
        var dispatched = new List<EdictEvent>();

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store), NoWriters, TimeProvider.System);
        await executor.ExecuteAsync(
            entry, NullStreamProvider.Instance, e => { dispatched.Add(e); return Task.FromResult<OutboxEntry?>(null); }, consumerType: typeof(object), liveWireEvent: null);

        await Verify(dispatched).DontScrubGuids();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnStagedEffect_WhenDeferredDispatchStagesOne()
    {
        // Arrange
        var edictEvent = new OrderPlacedEvent(
            OrderId: new Guid("44444444-4444-4444-4444-444444444444"),
            Sku: "SAGA")
        {
            EventId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        };
        var envelope = EnvelopeCodec.WrapInline(Serializer.SerializeToArray<EdictEvent>(edictEvent));
        var entry = new OutboxEntry
        {
            EntryId = new Guid("99999999-9999-9999-9999-999999999999"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };
        var stagedEffect = new OutboxEntry
        {
            EntryId = new Guid("12121212-1212-1212-1212-121212121212"),
            Kind = OutboxEffectKind.SendCommand,
            Payload = [7],
        };

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store: null), NoWriters, TimeProvider.System);

        // Act
        var returned = await executor.ExecuteAsync(
            entry, NullStreamProvider.Instance,
            _ => Task.FromResult<OutboxEntry?>(stagedEffect),
            consumerType: typeof(object), liveWireEvent: null);

        // Assert
        Assert.Same(stagedEffect, returned);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSurfaceFetchFailure_WhenPointerEnvelopeBlobMissing()
    {
        var pointerId = new Guid("dddddddd-9999-8888-7777-666666666666");
        var envelope = EnvelopeCodec.WrapPointer(pointerId);
        var entry = new OutboxEntry
        {
            EntryId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(new InMemoryClaimCheckStore()), NoWriters, TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            executor.ExecuteAsync(entry, NullStreamProvider.Instance, _ => Task.FromResult<OutboxEntry?>(null), consumerType: typeof(object), liveWireEvent: null));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNestInvocationSpanUnderCapturedTraceParent_WhenEntryCarriesTraceParent()
    {
        var capturedTraceId = "0123456789abcdef0123456789abcdef";
        var capturedSpanId = "fedcba9876543210";
        var traceParent = ActivityExtensions.BuildTraceParent(capturedTraceId, capturedSpanId);

        var edictEvent = new OrderPlacedEvent(
            OrderId: new Guid("33333333-3333-3333-3333-333333333333"),
            Sku: "TRACED")
        {
            EventId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        };
        var envelope = EnvelopeCodec.WrapInline(Serializer.SerializeToArray<EdictEvent>(edictEvent));
        var entry = new OutboxEntry
        {
            EntryId = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
            TraceParent = traceParent,
            TraceState = null,
        };

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store: null), NoWriters, TimeProvider.System);

        await executor.ExecuteAsync(entry, NullStreamProvider.Instance, _ => Task.FromResult<OutboxEntry?>(null), consumerType: typeof(object), liveWireEvent: null);

        // Filter by operation name — parallel tests sharing the process-wide
        // ActivityListener mechanism may surface unrelated edict.* spans here.
        var span = Assert.Single(stopped, a => a.OperationName == $"{SemanticConventions.Events.Spans.Handle} OrderPlacedEvent");
        Assert.Equal(capturedTraceId, span.TraceId.ToHexString());
        Assert.Equal(capturedSpanId, span.ParentSpanId.ToHexString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSurfaceHostThrow_WhenHandleThrows()
    {
        var edictEvent = new OrderPlacedEvent(
            OrderId: new Guid("22222222-2222-2222-2222-222222222222"),
            Sku: "FAULT")
        {
            EventId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        };
        var envelope = EnvelopeCodec.WrapInline(Serializer.SerializeToArray<EdictEvent>(edictEvent));
        var entry = new OutboxEntry
        {
            EntryId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store: null), NoWriters, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(entry, NullStreamProvider.Instance,
                _ => throw new InvalidOperationException("simulated dispatch failure"), consumerType: typeof(object), liveWireEvent: null));
    }

    static readonly IEventTagWriters NoWriters =
        new EventTagWriters(new Dictionary<Type, Action<EdictEvent, Activity>>());

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(InvokeHandlerExecutorTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    static ClaimCheckUnwrap BuildUnwrap(IEdictClaimCheckStore? store) =>
        new(Serializer, store);

    sealed class InMemoryClaimCheckStore : IEdictClaimCheckStore
    {
        public Dictionary<Guid, byte[]> Blobs { get; } = [];

        public Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException("invoke-handler executor tests never put");

        public Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken)
        {
            if (!Blobs.TryGetValue(eventId, out var bytes))
            {
                throw new KeyNotFoundException($"Claim-check blob '{eventId:N}' not found.");
            }
            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;

        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException("NullStreamProvider has no streams.");
    }
}
