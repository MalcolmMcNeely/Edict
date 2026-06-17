using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Runtime;

namespace Edict.Tests.Conformance.ClaimCheck;

// Stages a pointer-bearing EdictEventEnvelope directly through the durable
// consumer base — the same shape as ClaimCheckBlobMissingConsumer — but handles
// the inner event once the fetch succeeds. A transient-recovery scenario seeds
// the body in the store, holds the fetch down for a count-addressed number of
// attempts, then drives reminder ticks; the engine's per-entry retry eventually
// fetches and dispatches, so the event delivers rather than dead-lettering.
// Delivery and reminder ticks are remote methods so the drain is deterministic
// rather than waiting on Orleans' one-minute reminder floor.
public interface IClaimCheckTransientRecoveryConsumer : IGrainWithGuidKey
{
    Task DeliverAsync(EdictEvent edictEvent);

    Task ForceDrainViaReminderAsync();

    Task<int> GetHandledCountAsync();
}

[ImplicitStreamSubscription("ConformanceClaimCheckTransientRecovery")]
public sealed class ClaimCheckTransientRecoveryConsumer : EdictIdempotencyBase, IClaimCheckTransientRecoveryConsumer
{
    int _handled;

    public Task DeliverAsync(EdictEvent edictEvent) => OnEdictEventAsync(edictEvent);

    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());

    public Task<int> GetHandledCountAsync() => Task.FromResult(_handled);

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent)
    {
        _handled++;
        return Task.FromResult(EdictDispatchOutcome.HandledWithNoEffect);
    }
}
