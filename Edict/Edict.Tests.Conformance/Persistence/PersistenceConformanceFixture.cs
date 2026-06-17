using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.Time.Testing;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Substrate-agnostic surface every <em>persistence</em> provider's conformance
/// fixture exposes. A concrete subclass (e.g. <c>AzurePersistenceFixture</c>)
/// stands up a silo with a <strong>dumb deliver-once reference stream</strong>
/// (Orleans <c>MemoryStreams</c> — delivers once to implicit subscribers,
/// carries the <c>EventId</c> intact, never redelivers) and a
/// <strong>real persistence backend</strong> (real grain storage, real table
/// store, real dead-letter table, real claim-check store). The surface is the
/// axis-purity enforcement: a persistence scenario can only reach the send seam,
/// the grain probes, the durable table read seam, and the table write seam, so
/// it can never assert an ordering, partitioning, trace-propagation, or
/// transport-timing property of the stream — making the
/// <c>azure-queue-trace-propagation-gap</c> green-and-wrong failure mode
/// impossible by construction.
/// </summary>
public abstract class PersistenceConformanceFixture : IAsyncLifetime
{
    public abstract IEdictSender Sender { get; }

    public abstract IGrainFactory GrainFactory { get; }

    /// <summary>
    /// The fault switch this fixture's silo wires into its
    /// <see cref="ControllableOutboxExecutor"/> (when the fixture replaces the
    /// publish executor). An outbox scenario flips it to stage a publish crash;
    /// the instance is fixture-owned, so peer fixtures never see the flip.
    /// </summary>
    readonly FakeTimeProvider _virtualClock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Opt-in deterministic clock. A fixture whose scenarios drive the engine's
    /// cap, backoff, or reminder timing overrides this <c>true</c>; the provider
    /// configurator then registers <see cref="VirtualClock"/> as the silo's
    /// <see cref="TimeProvider"/> ahead of the host's <see cref="TimeProvider.System"/>
    /// default. Every other fixture leaves it <c>false</c> and runs on the wall
    /// clock, so backoff-gated recovery scenarios still elapse in real time.
    /// </summary>
    protected virtual bool UsesVirtualClock => false;

    /// <summary>
    /// The virtual clock the provider configurator registers when
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

    public OutboxFaultState OutboxFault { get; } = new();

    /// <summary>
    /// The fault switch this fixture's silo wires into its
    /// <see cref="ControllableGrainStorage"/> (when the fixture decorates grain
    /// storage). A write-fault scenario flips it to fault a grain-state write;
    /// the instance is fixture-owned, so peer fixtures never see the flip.
    /// </summary>
    public StorageFaultState StorageFault { get; } = new();

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();

    /// <summary>
    /// Provider-bound store seam used by table-projection and dead-letter
    /// conformance scenarios to verify a row landed in the substrate's durable
    /// store. Reads go store-direct (the right shape for verifying a write
    /// landed, independent of the grain read path). Each substrate fixture
    /// returns its own <see cref="IEdictTableWriteStore{T}"/>; the scenario stays
    /// substrate-neutral.
    /// </summary>
    public abstract IEdictTableWriteStore<T> GetTableStore<T>(string tableName) where T : class, new();

    /// <summary>
    /// Provider-bound write seam used by table-backed conformance scenarios to
    /// seed rows before exercising a read contract. Mirrors the silo-side
    /// <see cref="IEdictTableStoreFactory"/> registration, but constructed
    /// against the test process so seeding does not round-trip through the
    /// cluster.
    /// </summary>
    public abstract IEdictTableStoreFactory TableStoreFactory { get; }
}
