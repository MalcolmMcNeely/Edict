using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.ClaimCheck;

// slice 4: the receiver-side unwrap registered by AddEdict() must
// suppress the blob fetch for EdictDeadLetterProjectionBuilder so a
// pointer-bearing envelope on the dead-letter stream is never auto-fetched
// into a 32 KB property — the framework consumer is the one type that holds
// the pointer instead of inflating the body. Every other consumer type still
// fetches by default.
public sealed class ClaimCheckUnwrapDeadLetterPredicateTests
{
    [Fact]
    public async Task ApplyAsync_ShouldReturnEnvelopeUnfetched_WhenConsumerIsDeadLetterProjectionBuilder()
    {
        var unwrap = BuildUnwrap(out var store);
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "edict-claim-check/oversized");

        var result = await unwrap.ApplyAsync(
            envelope,
            consumerType: typeof(EdictDeadLetterProjectionBuilder),
            CancellationToken.None);

        Assert.Same(envelope, result);
        Assert.Empty(store.Gets);
    }

    [Fact]
    public async Task ApplyAsync_ShouldFetch_WhenConsumerIsAnyOtherType()
    {
        var unwrap = BuildUnwrap(out var store);
        var inner = new OrderPlacedEvent(Guid.NewGuid(), "SKU-OTHER")
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var serializer = BuildServices(store).GetRequiredService<Serializer>();
        store.Blobs["edict-claim-check/other"] = serializer.SerializeToArray<EdictEvent>(inner);
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "edict-claim-check/other");

        var result = await unwrap.ApplyAsync(
            envelope,
            consumerType: typeof(SomeOtherProjection),
            CancellationToken.None);

        Assert.Equal(["edict-claim-check/other"], store.Gets.Select(g => g.Key));
        var materialised = Assert.IsType<OrderPlacedEvent>(result);
        Assert.Equal(inner.OrderId, materialised.OrderId);
    }

    sealed class SomeOtherProjection;

    static ClaimCheckUnwrap BuildUnwrap(out RecordingStore store)
    {
        store = new RecordingStore();
        var sp = BuildServices(store);
        return sp.GetRequiredService<ClaimCheckUnwrap>();
    }

    static IServiceProvider BuildServices(RecordingStore store)
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(ClaimCheckUnwrapDeadLetterPredicateTests).Assembly);
            b.AddEdictContractSerializer();
        });
        services.AddSingleton<IEdictClaimCheckStore>(store);
        services.AddEdict();
        return services.BuildServiceProvider();
    }

    sealed record GetCall(string Key);

    sealed class RecordingStore : IEdictClaimCheckStore
    {
        public List<GetCall> Gets { get; } = [];
        public Dictionary<string, byte[]> Blobs { get; } = [];

        public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken ct) =>
            throw new NotSupportedException("predicate tests never put");

        public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken ct)
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
