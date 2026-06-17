using System.Collections.Concurrent;

using Edict.Contracts.Projections;
using Edict.Contracts.Sending;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Outbox;
using Edict.Tests.Conformance.Tenancy;

using Orleans;

using Xunit;

namespace Edict.Pairing.Tests;

/// <summary>
/// The surface the bucket-4 pairing smoke runs against: a silo wiring
/// <strong>both real backends</strong> of one shipped pairing (a real stream
/// provider and a real persistence provider), with the <c>edict-state</c> store
/// wrapped in <see cref="Edict.Tests.Conformance.Outbox.ControllableGrainStorage"/>
/// so the write-fault∧redelivery conjunction can fault a real store write and let
/// the real stream redeliver. Unlike an axis battery, this binding asserts the
/// irreducible interaction neither single-axis fixture can produce: the
/// documented stack booting and round-tripping, and a real store fault conjoined
/// with a real stream redelivery.
/// </summary>
public abstract class PairingFixture : IAsyncLifetime
{
    public abstract IEdictSender Sender { get; }

    public abstract IGrainFactory GrainFactory { get; }

    /// <summary>
    /// The fault switch this fixture's silo wires into the
    /// <see cref="ControllableGrainStorage"/> over the real <c>edict-state</c>
    /// store. The write-fault∧redelivery scenario
    /// flips it to fault a real grain-state write; the instance is fixture-owned,
    /// so peer pairings never see the flip.
    /// </summary>
    public StorageFaultState StorageFault { get; } = new();

    /// <summary>
    /// The per-fixture ambient tenant the silo and client tenant resolvers read.
    /// Owned by the fixture rather than a process-wide static so the two shipped
    /// pairings, which run as parallel collections, never race each other's wall;
    /// the tenant round-trip scenario sets it to act as each tenant in turn.
    /// </summary>
    public TenantAmbient TenantAmbient { get; } = new();

    /// <summary>
    /// The ambient-scoped reader the tenant round-trip reads through, resolved from
    /// the client once tenancy is wired. Proves the composed-key stream routing and
    /// the tenant-partitioned persistence land a tenant's rows in its own wall
    /// across <strong>both</strong> real backends at once — the conjunction neither
    /// single-axis battery can show.
    /// </summary>
    public abstract IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> EmployeeDirectoryReader { get; }

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();
}

/// <summary>
/// A per-fixture mutable holder for the ambient tenant. The silo and client tenant
/// resolvers close over one instance, so a scenario sets the wall it acts as on the
/// fixture and both the origin send and the scoped read see it, with no process-wide
/// static for a parallel peer pairing to clobber.
/// </summary>
public sealed class TenantAmbient
{
    public EdictTenantId? Current { get; set; }
}

/// <summary>
/// Hands a per-fixture context to the Orleans <c>ISiloConfigurator</c>, which is
/// instantiated fresh by the test host and can only read a string key off silo
/// configuration. One closed generic per context type isolates the two pairings.
/// </summary>
static class PairingContextRegistry<TContext>
{
    public const string ContextKeyProperty = "PairingContextKey";

    static readonly ConcurrentDictionary<string, TContext> _contexts = new();

    public static string Register(TContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _contexts[key] = context;
        return key;
    }

    public static TContext Get(string key) => _contexts[key];

    public static void Unregister(string key) => _contexts.TryRemove(key, out _);
}
