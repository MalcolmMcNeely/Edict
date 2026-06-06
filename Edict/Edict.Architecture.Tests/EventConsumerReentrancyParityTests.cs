using System.Reflection;

using Edict.Core.Idempotency;

using Xunit;

namespace Edict.Architecture.Tests;

// Fidelity guard pinning the two event-delivery seams to one reentrancy contract.
// Production delivers through the stream observer OnNextAsync; the in-memory Test
// Framework delivers through IEdictEventConsumer.OnEdictEventAsync. A real Orleans
// pulling agent delivers a single stream's events to one consumer activation
// serially (it awaits each OnNextAsync before the next), and OnNextAsync is
// non-reentrant, so distinct events never run concurrently on one activation.
// Marking the harness seam [AlwaysInterleave] reintroduces exactly the concurrent
// delivery that corrupted list projections — so the harness seam must stay
// non-reentrant, matching production. This guard fails the moment the two seams
// diverge.
public class EventConsumerReentrancyParityTests
{
    const string AlwaysInterleaveFullName = "Orleans.Concurrency.AlwaysInterleaveAttribute";

    [Fact]
    public void HarnessDeliverySeam_IsNotInterleaving()
    {
        var harnessSeam = typeof(IEdictEventConsumer).GetMethod(nameof(IEdictEventConsumer.OnEdictEventAsync))!;

        Assert.False(
            IsInterleaving(harnessSeam),
            "IEdictEventConsumer.OnEdictEventAsync must not be [AlwaysInterleave]: a real Orleans pulling agent "
            + "delivers a single stream's events to one consumer serially, so the harness delivery seam must stay "
            + "non-reentrant to match production's OnNextAsync. Interleaving it manufactures concurrent deliveries "
            + "that real Orleans never produces.");
    }

    [Fact]
    public void HarnessAndProductionDeliverySeams_ShareOneReentrancyContract()
    {
        var harnessSeam = typeof(IEdictEventConsumer).GetMethod(nameof(IEdictEventConsumer.OnEdictEventAsync))!;
        var productionSeam = typeof(EdictIdempotencyBase).GetMethod(nameof(EdictIdempotencyBase.OnNextAsync))!;

        Assert.Equal(IsInterleaving(productionSeam), IsInterleaving(harnessSeam));
    }

    static bool IsInterleaving(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute.GetType().FullName == AlwaysInterleaveFullName);
}
