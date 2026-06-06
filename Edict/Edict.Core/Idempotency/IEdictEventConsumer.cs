using Edict.Contracts.Events;

namespace Edict.Core.Idempotency;

/// <summary>
/// Brand-prefixed grain-interface seam every event-consuming grain shares
/// (event handlers, projection builders, sagas — the inheritance root shared
/// by the consumer-facing grain bases, brand-rule clause b). One
/// method, <see cref="OnEdictEventAsync"/>, is the unified delivery entry that
/// the in-memory Test Framework's in-process stream-provider replacement
/// invokes per publish — the production Orleans memory-stream pulling agent
/// fails to deliver to referenced-assembly consumers, so <c>Edict.Testing</c>
/// routes through this seam instead.
/// <para>
/// The method is non-reentrant, matching the production stream-delivery entry
/// <c>OnNextAsync</c>: a real Orleans pulling agent delivers one stream's events
/// to a single consumer activation serially, awaiting each before the next, so
/// distinct events for one activation never run concurrently. The Test Framework
/// serializes its own deliveries to each activation to mirror that, so the two
/// delivery seams honour one identical reentrancy contract.
/// </para>
/// </summary>
public interface IEdictEventConsumer : IGrainWithGuidKey
{
    Task OnEdictEventAsync(EdictEvent edictEvent);
}
