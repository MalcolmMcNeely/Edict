using Edict.Contracts.Sending;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Substrate-agnostic surface every <em>streaming</em> provider's conformance
/// fixture exposes. A concrete subclass (e.g. <c>AqsStreamingFixture</c>) stands
/// up a silo with a <strong>real</strong> stream provider behind <c>"edict"</c>
/// and <strong>reference persistence</strong> (Orleans memory grain storage plus
/// the in-memory <c>ReferenceClaimCheckStore</c> / <c>ReferenceTableStoreFactory</c>);
/// it never touches a real store. The surface is the axis-purity enforcement: a
/// streaming scenario can only reach the publish seam, the grain probes, and the
/// claim-check existence probe, so it physically cannot assert a real-persistence
/// property.
/// </summary>
public abstract class StreamingConformanceFixture : IAsyncLifetime
{
    public abstract IEdictSender Sender { get; }

    public abstract IGrainFactory GrainFactory { get; }

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();

    /// <summary>
    /// True when the reference claim-check store holds a body under the given
    /// event id. The pointer-branch streaming scenario asserts the body landed
    /// in the store after a publish — a streaming-publish property — without
    /// touching a provider SDK or asserting a durable-store property.
    /// </summary>
    public abstract Task<bool> ClaimCheckBlobExistsAsync(Guid eventId);
}
