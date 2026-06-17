using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Contracts.Tenancy;
using Edict.Core.ClaimCheck;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.ClaimCheck;

[Collection(TestSupport.EdictListenerUnitCollection.Name)]
public sealed class ClaimCheckUnwrapTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ApplyAsync_ShouldReturnSameEventAndSkipStore_WhenIncomingIsNotEnvelope()
    {
        var store = new RecordingStore();
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), "SKU-PLAIN");
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var result = await unwrap.ApplyAsync(edictEvent, consumerType: typeof(object), parentContext: default, CancellationToken.None);

        Assert.Same(edictEvent, result);
        Assert.Empty(store.Gets);
    }

    [Fact]
    public async Task ApplyAsync_ShouldFetchBlobAndReturnInnerEvent_WhenEnvelopeCarriesPointer()
    {
        var inner = new OrderPlacedEvent(Guid.NewGuid(), "SKU-FETCH")
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var innerBytes = Serializer.SerializeToArray<EdictEvent>(inner);
        var fetchId = Guid.NewGuid();
        var store = new RecordingStore();
        store.Blobs[fetchId] = innerBytes;
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: fetchId);
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var result = await unwrap.ApplyAsync(envelope, consumerType: typeof(object), parentContext: default, CancellationToken.None);

        Assert.Equal([fetchId], store.Gets.Select(g => g.EventId));
        var materialised = Assert.IsType<OrderPlacedEvent>(result);
        Assert.Equal(inner.OrderId, materialised.OrderId);
        Assert.Equal(inner.Sku, materialised.Sku);
        Assert.Equal(inner.EventId, materialised.EventId);
    }

    [Fact]
    public async Task ApplyAsync_ShouldDeserialiseFromInlineAndSkipStore_WhenEnvelopeCarriesInlinePayload()
    {
        var inner = new OrderPlacedEvent(Guid.NewGuid(), "SKU-INLINE")
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var innerBytes = Serializer.SerializeToArray<EdictEvent>(inner);
        var envelope = new EdictEventEnvelope(inlinePayload: innerBytes, eventId: Guid.NewGuid());
        var store = new RecordingStore();
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var result = await unwrap.ApplyAsync(envelope, consumerType: typeof(object), parentContext: default, CancellationToken.None);

        Assert.Empty(store.Gets);
        var materialised = Assert.IsType<OrderPlacedEvent>(result);
        Assert.Equal(inner.OrderId, materialised.OrderId);
        Assert.Equal(inner.Sku, materialised.Sku);
        Assert.Equal(inner.EventId, materialised.EventId);
    }

    [Fact]
    public async Task WrapThenUnwrap_ShouldMaterialiseInnerEventWithIdEqualToEnvelopeId_WhenOversized()
    {
        var store = new InMemoryClaimCheckStore();
        var stampedId = Guid.NewGuid();
        var inner = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256)) { EventId = stampedId };
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());

        var wrapped = await policy.ApplyAsync(inner, default, CancellationToken.None);
        var envelope = Assert.IsType<EdictEventEnvelope>(wrapped.WireEvent);
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var materialised = await unwrap.ApplyAsync(envelope, consumerType: typeof(object), parentContext: default, CancellationToken.None);

        // The dedup-committed id (envelope) and the handler-visible id (unwrapped
        // inner) are one identity for life: non-empty, and equal across the wrap.
        Assert.NotEqual(Guid.Empty, materialised.EventId);
        Assert.Equal(envelope.EventId, materialised.EventId);
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnEnvelopeUnfetched_WhenPredicateSuppressesFetchForConsumerType()
    {
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: Guid.NewGuid());
        var store = new RecordingStore();
        var unwrap = new ClaimCheckUnwrap(
            Serializer,
            store,
            shouldFetchForConsumer: t => t != typeof(OptedOutConsumer));

        var result = await unwrap.ApplyAsync(envelope, consumerType: typeof(OptedOutConsumer), parentContext: default, CancellationToken.None);

        Assert.Same(envelope, result);
        Assert.Empty(store.Gets);
    }

    sealed class OptedOutConsumer;

    [Fact]
    public async Task ApplyAsync_ShouldEmitClaimCheckGetSpan_WhenFetchBranchFires()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var inner = new OrderPlacedEvent(Guid.NewGuid(), "SKU-SPAN")
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var innerBytes = Serializer.SerializeToArray<EdictEvent>(inner);
        var fetchId = Guid.NewGuid();
        var store = new RecordingStore();
        store.Blobs[fetchId] = innerBytes;
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: fetchId);
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        await unwrap.ApplyAsync(envelope, consumerType: typeof(object), parentContext: default, CancellationToken.None);

        var get = stopped.Single(a =>
            a.OperationName == SemanticConventions.ClaimCheck.Spans.Get
            && fetchId.ToString().Equals(a.GetTagItem(SemanticConventions.ClaimCheck.Tags.Key)));
        Assert.Equal(nameof(OrderPlacedEvent), get.GetTagItem(SemanticConventions.Events.Tags.Type));
        Assert.Equal(innerBytes.Length, get.GetTagItem(SemanticConventions.Events.Tags.SizeBytes));
        Assert.Equal(fetchId.ToString(), get.GetTagItem(SemanticConventions.ClaimCheck.Tags.Key));
    }

    [Fact]
    public async Task ApplyAsync_ShouldNestGetSpanUnderProvidedParent_NotAmbient()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var inner = new OrderPlacedEvent(Guid.NewGuid(), "SKU-GET-PARENT")
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var innerBytes = Serializer.SerializeToArray<EdictEvent>(inner);
        var fetchId = Guid.NewGuid();
        var store = new RecordingStore();
        store.Blobs[fetchId] = innerBytes;
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: fetchId);
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        // The consumer turn (edict.event.handle) is supplied explicitly; with no
        // ambient activity, the GET must nest under it rather than orphaning.
        var handleContext = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        Assert.Null(Activity.Current);

        await unwrap.ApplyAsync(envelope, typeof(object), handleContext, CancellationToken.None);

        // Scope to this test's GET by its unique key: the process-wide listener
        // also catches GET spans from other claim-check unit classes run in parallel.
        var get = stopped.Single(a =>
            a.OperationName == SemanticConventions.ClaimCheck.Spans.Get
            && fetchId.ToString().Equals(a.GetTagItem(SemanticConventions.ClaimCheck.Tags.Key)));
        Assert.Equal(handleContext.TraceId, get.TraceId);
        Assert.Equal(handleContext.SpanId, get.ParentSpanId);
    }

    [Fact]
    public async Task ApplyAsync_ShouldNotEmitGetSpan_WhenPathDoesNotFetch()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var inner = new OrderPlacedEvent(Guid.NewGuid(), "SKU-NOSPAN")
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var innerBytes = Serializer.SerializeToArray<EdictEvent>(inner);
        var unwrap = new ClaimCheckUnwrap(Serializer, store: null);

        // Non-envelope passthrough and inline-branch deserialisation must both
        // skip the fetch span: it advertises a store I/O event that didn't happen.
        await unwrap.ApplyAsync(inner, consumerType: typeof(object), parentContext: default, CancellationToken.None);
        await unwrap.ApplyAsync(
            new EdictEventEnvelope(inlinePayload: innerBytes, eventId: Guid.NewGuid()),
            consumerType: typeof(object),
            parentContext: default,
            CancellationToken.None);

        Assert.DoesNotContain(stopped, a => a.OperationName == SemanticConventions.ClaimCheck.Spans.Get);
    }

    [Fact]
    public async Task ApplyAsync_ShouldSurfaceStoreException_WhenBlobIsMissing()
    {
        // The unwrap must not catch — EdictIdempotencyBase is what funnels a
        // missing blob into receiver-side dead-letter promotion.
        var missingId = Guid.NewGuid();
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: missingId);
        var store = new RecordingStore();
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => unwrap.ApplyAsync(envelope, consumerType: typeof(object), parentContext: default, CancellationToken.None));

        Assert.Contains(missingId.ToString("N"), exception.Message);
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(ClaimCheckUnwrapTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed record GetCall(Guid EventId);

    sealed class RecordingStore : IEdictClaimCheckStore
    {
        public List<GetCall> Gets { get; } = [];
        public Dictionary<Guid, byte[]> Blobs { get; } = [];

        public Task PutAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException("receiver-side tests never put");

        public Task<ReadOnlyMemory<byte>> GetAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken)
        {
            Gets.Add(new GetCall(eventId));
            if (!Blobs.TryGetValue(eventId, out var bytes))
            {
                throw new KeyNotFoundException($"Claim-check blob '{eventId:N}' not found.");
            }
            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }
}
