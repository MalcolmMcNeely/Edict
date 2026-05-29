using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Serialization;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.ClaimCheck;

public sealed class ClaimCheckUnwrapTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ApplyAsync_ShouldReturnSameEventAndSkipStore_WhenIncomingIsNotEnvelope()
    {
        var store = new RecordingStore();
        var edictEvent = new OrderPlacedEvent(Guid.NewGuid(), "SKU-PLAIN");
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var result = await unwrap.ApplyAsync(edictEvent, consumerType: typeof(object), CancellationToken.None);

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
        var store = new RecordingStore();
        store.Blobs["blob-key-fetch"] = innerBytes;
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "blob-key-fetch");
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var result = await unwrap.ApplyAsync(envelope, consumerType: typeof(object), CancellationToken.None);

        Assert.Equal(["blob-key-fetch"], store.Gets.Select(g => g.Key));
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
        var envelope = new EdictEventEnvelope(inlinePayload: innerBytes, claimCheckKey: null);
        var store = new RecordingStore();
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var result = await unwrap.ApplyAsync(envelope, consumerType: typeof(object), CancellationToken.None);

        Assert.Empty(store.Gets);
        var materialised = Assert.IsType<OrderPlacedEvent>(result);
        Assert.Equal(inner.OrderId, materialised.OrderId);
        Assert.Equal(inner.Sku, materialised.Sku);
        Assert.Equal(inner.EventId, materialised.EventId);
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnEnvelopeUnfetched_WhenPredicateSuppressesFetchForConsumerType()
    {
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "blob-suppressed");
        var store = new RecordingStore();
        var unwrap = new ClaimCheckUnwrap(
            Serializer,
            store,
            shouldFetchForConsumer: t => t != typeof(OptedOutConsumer));

        var result = await unwrap.ApplyAsync(envelope, consumerType: typeof(OptedOutConsumer), CancellationToken.None);

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
        var store = new RecordingStore();
        store.Blobs["blob-key-span"] = innerBytes;
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "blob-key-span");
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        await unwrap.ApplyAsync(envelope, consumerType: typeof(object), CancellationToken.None);

        var get = stopped.Single(a => a.OperationName == SemanticConventions.ClaimCheck.Spans.Get);
        Assert.Equal(nameof(OrderPlacedEvent), get.GetTagItem(SemanticConventions.Events.Tags.Type));
        Assert.Equal(innerBytes.Length, get.GetTagItem(SemanticConventions.Events.Tags.SizeBytes));
        Assert.Equal("blob-key-span", get.GetTagItem(SemanticConventions.ClaimCheck.Tags.Key));
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
        await unwrap.ApplyAsync(inner, consumerType: typeof(object), CancellationToken.None);
        await unwrap.ApplyAsync(
            new EdictEventEnvelope(inlinePayload: innerBytes, claimCheckKey: null),
            consumerType: typeof(object),
            CancellationToken.None);

        Assert.DoesNotContain(stopped, a => a.OperationName == SemanticConventions.ClaimCheck.Spans.Get);
    }

    [Fact]
    public async Task ApplyAsync_ShouldSurfaceStoreException_WhenBlobIsMissing()
    {
        // The unwrap must not catch — EdictIdempotencyBase is what funnels a
        // missing blob into receiver-side dead-letter promotion.
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "blob-missing");
        var store = new RecordingStore();
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => unwrap.ApplyAsync(envelope, consumerType: typeof(object), CancellationToken.None));

        Assert.Contains("blob-missing", exception.Message);
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

    sealed record GetCall(string Key);

    sealed class RecordingStore : IEdictClaimCheckStore
    {
        public List<GetCall> Gets { get; } = [];
        public Dictionary<string, byte[]> Blobs { get; } = [];

        public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException("receiver-side tests never put");

        public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken cancellationToken)
        {
            Gets.Add(new GetCall(key));
            if (!Blobs.TryGetValue(key, out var bytes))
            {
                throw new KeyNotFoundException($"Claim-check blob '{key}' not found.");
            }
            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }
}
