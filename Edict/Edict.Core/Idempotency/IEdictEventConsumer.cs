using Edict.Contracts.Events;

using Orleans.Concurrency;

namespace Edict.Core.Idempotency;

/// <summary>
/// Brand-prefixed grain-interface seam every event-consuming grain shares
/// (event handlers, projection builders, sagas — the inheritance root shared
/// by the consumer-facing grain bases, brand-rule clause b). One
/// method, <see cref="OnEdictEventAsync"/>, is the unified delivery entry that
/// the in-memory Test Framework's in-process stream-provider replacement
/// invokes synchronously per publish — the production Orleans memory-stream
/// pulling agent fails to deliver to referenced-assembly consumers, so
/// <c>Edict.Testing</c> routes through this seam instead.
/// <para>
/// <see cref="AlwaysInterleaveAttribute"/> mirrors Orleans's own stream
/// extension which is interleaving: a saga or projection that re-enters its own
/// grain via a fan-out cascade must not deadlock on grain-turn reentrancy.
/// </para>
/// </summary>
public interface IEdictEventConsumer : IGrainWithGuidKey
{
    [AlwaysInterleave]
    Task OnEdictEventAsync(EdictEvent edictEvent);
}
