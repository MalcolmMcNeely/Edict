using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.ClaimCheck;

public sealed class ClaimCheckUnwrapDeadLetterPredicateTests
{
    [Fact]
    public async Task ApplyAsync_ShouldReturnEnvelopeUnfetched_WhenConsumerIsDeadLetterProjectionBuilder()
    {
        var unwrap = BuildUnwrap(out var store);
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: Guid.NewGuid());

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
        var fetchId = Guid.NewGuid();
        store.Blobs[fetchId] = serializer.SerializeToArray<EdictEvent>(inner);
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: fetchId);

        var result = await unwrap.ApplyAsync(
            envelope,
            consumerType: typeof(SomeOtherProjection),
            CancellationToken.None);

        Assert.Equal([fetchId], store.Gets.Select(g => g.EventId));
        var materialised = Assert.IsType<OrderPlacedEvent>(result);
        Assert.Equal(inner.OrderId, materialised.OrderId);
    }

    sealed class SomeOtherProjection;

    static ClaimCheckUnwrap BuildUnwrap(out RecordingStore store)
    {
        store = new RecordingStore();
        var serviceProvider = BuildServices(store);
        return serviceProvider.GetRequiredService<ClaimCheckUnwrap>();
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

    sealed record GetCall(Guid EventId);

    sealed class RecordingStore : IEdictClaimCheckStore
    {
        public List<GetCall> Gets { get; } = [];
        public Dictionary<Guid, byte[]> Blobs { get; } = [];

        public Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            throw new NotSupportedException("predicate tests never put");

        public Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken)
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
