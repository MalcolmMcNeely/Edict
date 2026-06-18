using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.Time.Testing;

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

    /// <summary>
    /// The fault switch this fixture's silo wires into the
    /// <see cref="ControllableGrainStorage"/> over the reference grain storage.
    /// The redeliver-after-throw scenario flips it to fault a consumer turn out
    /// to the stream layer; the instance is fixture-owned, so peer tests in the
    /// collection never see the flip.
    /// </summary>
    public StorageFaultState StorageFault { get; } = new();

    readonly FakeTimeProvider _virtualClock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Opt-in deterministic clock. A fixture whose scenarios drive the engine's
    /// cap, backoff, or reminder timing overrides this <c>true</c>; the silo
    /// configurator then registers <see cref="VirtualClock"/> as the silo's
    /// <see cref="TimeProvider"/> ahead of the host's <see cref="TimeProvider.System"/>
    /// default. Every other fixture leaves it <c>false</c> and runs on the wall
    /// clock, so backoff-gated recovery scenarios still elapse in real time.
    /// </summary>
    protected virtual bool UsesVirtualClock => false;

    /// <summary>
    /// The virtual clock the silo configurator registers when
    /// <see cref="UsesVirtualClock"/> is set. <see cref="AdvanceClock"/> pushes it
    /// forward; the silo reads it as its <see cref="TimeProvider"/>.
    /// </summary>
    protected TimeProvider VirtualClock => _virtualClock;

    /// <summary>
    /// Advances the fixture's virtual clock past a cap, backoff, or deadline
    /// instant so a scenario can fire the engine's time-gated work deterministically
    /// with no wall-clock wait. It moves the silo's clock only when the fixture
    /// opted in via <see cref="UsesVirtualClock"/>.
    /// </summary>
    public void AdvanceClock(TimeSpan by) => _virtualClock.Advance(by);

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();

    /// <summary>
    /// True when the reference claim-check store holds a body under the given
    /// event id. The pointer-branch streaming scenario asserts the body landed
    /// in the store after a publish — a streaming-publish property — without
    /// touching a provider SDK or asserting a durable-store property.
    /// </summary>
    public abstract Task<bool> ClaimCheckBlobExistsAsync(Guid eventId);

    /// <summary>
    /// Typed read accessor over the fixture's in-memory reference table store —
    /// the same store the silo's projection grains write through. The cursor-over-
    /// stream scenario uses it to wait deterministically for the real-stream-
    /// delivered event to land as the List-projection row before reading through
    /// the cursor, so the cursor read is never racing real-stream delivery latency.
    /// Reads stay in-process: a row read is an in-grain property, not a real-store
    /// property, so it does not breach the streaming axis's purity rule.
    /// </summary>
    public abstract IEdictTableWriteStore<T> GetTableStore<T>(string tableName) where T : class, new();
}
