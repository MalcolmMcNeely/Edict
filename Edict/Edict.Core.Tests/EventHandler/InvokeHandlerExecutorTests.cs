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

// InvokeHandlerExecutor unit tests against the ADR-0026-widened payload
// shape: the buffered EdictEvent is always an EdictEventEnvelope (inline or
// pointer), and the executor calls ClaimCheckUnwrap.ApplyAsync before
// invoking the deferred-dispatch callback. No grain, no cluster: the
// callback is replaced by a fake so the executor's logic body is the only
// thing under test.
public sealed class InvokeHandlerExecutorTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ExecuteAsync_ShouldDispatchInnerEvent_WhenInlineEnvelopeEntry()
    {
        // The common case after ADR 0026: EventHandler's stream callback wrapped
        // a concrete event into an inline-payload envelope before staging.
        var evt = new OrderPlacedEvent(
            OrderId: new Guid("11111111-1111-1111-1111-111111111111"),
            Sku: "WIDGET")
        {
            EventId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        };
        var inlineBytes = Serializer.SerializeToArray<EdictEvent>(evt);
        var envelope = EnvelopeCodec.WrapInline(inlineBytes);
        var entry = new OutboxEntry
        {
            EntryId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };
        var dispatched = new List<EdictEvent>();

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store: null));
        await executor.ExecuteAsync(
            entry, NullStreamProvider.Instance, e => { dispatched.Add(e); return Task.CompletedTask; }, consumerType: typeof(object));

        await Verify(dispatched).DontScrubGuids();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFetchAndDispatchInnerEvent_WhenPointerEnvelopeEntry()
    {
        // ADR 0026 fold: pointer-bearing envelope entries (the receiver-side
        // path for oversized events) run the same executor; ClaimCheckUnwrap
        // hits the store, materialises the inner event, and the deferred
        // dispatch fires against the concrete event.
        var inner = new OrderPlacedEvent(
            OrderId: new Guid("22222222-2222-2222-2222-222222222222"),
            Sku: "OVERSIZED")
        {
            EventId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        };
        var key = "edict-claim-check/oversized";
        var store = new InMemoryClaimCheckStore();
        store.Blobs[key] = Serializer.SerializeToArray<EdictEvent>(inner);

        var envelope = EnvelopeCodec.WrapPointer(key);
        var entry = new OutboxEntry
        {
            EntryId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };
        var dispatched = new List<EdictEvent>();

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store));
        await executor.ExecuteAsync(
            entry, NullStreamProvider.Instance, e => { dispatched.Add(e); return Task.CompletedTask; }, consumerType: typeof(object));

        await Verify(dispatched).DontScrubGuids();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSurfaceFetchFailure_WhenPointerEnvelopeBlobMissing()
    {
        // The engine catches the throw, bumps backoff via the per-entry retry
        // path, and (at MaxAttempts) promotes via the standard IDeadLetterPromoter
        // path. The executor's job ends with the rethrow.
        var key = "edict-claim-check/missing";
        var envelope = EnvelopeCodec.WrapPointer(key);
        var entry = new OutboxEntry
        {
            EntryId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(new InMemoryClaimCheckStore()));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            executor.ExecuteAsync(entry, NullStreamProvider.Instance, _ => Task.CompletedTask, consumerType: typeof(object)));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNestInvocationSpanUnderCapturedTraceParent_WhenEntryCarriesTraceParent()
    {
        var capturedTraceId = "0123456789abcdef0123456789abcdef";
        var capturedSpanId = "fedcba9876543210";
        var traceParent = ActivityExtensions.BuildTraceParent(capturedTraceId, capturedSpanId);

        var evt = new OrderPlacedEvent(
            OrderId: new Guid("33333333-3333-3333-3333-333333333333"),
            Sku: "TRACED")
        {
            EventId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        };
        var envelope = EnvelopeCodec.WrapInline(Serializer.SerializeToArray<EdictEvent>(evt));
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

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store: null));

        await executor.ExecuteAsync(entry, NullStreamProvider.Instance, _ => Task.CompletedTask, consumerType: typeof(object));

        // Filter by operation name — parallel tests sharing the process-wide
        // ActivityListener mechanism may surface unrelated edict.* spans here.
        var span = Assert.Single(stopped, a => a.OperationName == "edict.event.handle OrderPlacedEvent");
        Assert.Equal(capturedTraceId, span.TraceId.ToHexString());
        Assert.Equal(capturedSpanId, span.ParentSpanId.ToHexString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSurfaceHostThrow_WhenHandleThrows()
    {
        var evt = new OrderPlacedEvent(
            OrderId: new Guid("22222222-2222-2222-2222-222222222222"),
            Sku: "FAULT")
        {
            EventId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        };
        var envelope = EnvelopeCodec.WrapInline(Serializer.SerializeToArray<EdictEvent>(evt));
        var entry = new OutboxEntry
        {
            EntryId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };

        var executor = new InvokeHandlerExecutor(Serializer, BuildUnwrap(store: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(entry, NullStreamProvider.Instance,
                _ => throw new InvalidOperationException("simulated dispatch failure"), consumerType: typeof(object)));
    }

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
        public Dictionary<string, byte[]> Blobs { get; } = [];

        public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken ct) =>
            throw new NotSupportedException("invoke-handler executor tests never put");

        public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken ct)
        {
            if (!Blobs.TryGetValue(key, out var bytes))
            {
                throw new KeyNotFoundException($"Claim-check blob '{key}' not found.");
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
