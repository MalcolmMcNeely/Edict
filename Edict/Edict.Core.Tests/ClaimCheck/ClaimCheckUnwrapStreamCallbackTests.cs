using Edict.Contracts.Events;
using Edict.Core.Serialization;
using Edict.Core.Tests.Grains;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// End-to-end stream-callback proof for the receiver-side claim-check unwrap
/// (ADR 0024, slice 3): publishing a pointer-bearing <see cref="EdictEventEnvelope"/>
/// down the implicit-subscription stream lands the inner concrete event on
/// the consumer's <c>Handle</c>; the consumer never observes the envelope.
/// </summary>
[Collection(EdictClusterCollection.Name)]
public sealed class ClaimCheckUnwrapStreamCallbackTests(EdictClusterFixture fixture)
{
    [Fact]
    public async Task OnStreamEvent_ShouldDispatchInnerEvent_WhenEnvelopeCarriesPointer()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IDedupTestConsumer>(grainId);

        var inner = new DedupTestEvent(grainId, 42) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var serializer = fixture.Cluster.ServiceProvider.GetRequiredService<Serializer>();
        var innerBytes = serializer.SerializeToArray<EdictEvent>(inner);
        var key = $"edict-claim-check/{Guid.NewGuid():N}";
        fixture.ClaimCheckStore.Seed(key, innerBytes);

        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: key)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(envelope);

        var handled = await WaitForHandledAsync(consumer);
        Assert.Single(handled);
        // The dedup ring commits against the materialised inner event's
        // EventId — not the envelope's wire-frame EventId.
        Assert.Equal(inner.EventId, handled[0]);
    }

    static async Task<IReadOnlyList<Guid>> WaitForHandledAsync(IDedupTestConsumer grain)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await grain.GetHandledEventIdsAsync();
            if (ids.Count >= 1)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return await grain.GetHandledEventIdsAsync();
    }
}
