using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Runtime;

namespace Edict.Tests.Conformance.ClaimCheck;

// Stages a pointer-bearing EdictEventEnvelope directly through the durable
// consumer base so a conformance scenario can drive a missing-claim-check
// fetch to dead-letter on any substrate, without a real stream hop. Delivery
// and reminder ticks are exposed as remote methods so the drain is
// deterministic rather than waiting on Orleans' one-minute reminder floor.
public interface IClaimCheckBlobMissingConsumer : IGrainWithGuidKey
{
    Task DeliverAsync(EdictEvent edictEvent);

    Task ForceDrainViaReminderAsync();
}

[ImplicitStreamSubscription("ConformanceClaimCheckBlobMissing")]
public sealed class ClaimCheckBlobMissingConsumer : EdictIdempotencyBase, IClaimCheckBlobMissingConsumer
{
    public Task DeliverAsync(EdictEvent edictEvent) => OnEdictEventAsync(edictEvent);

    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent) =>
        Task.FromResult(EdictDispatchOutcome.NotHandled);
}
