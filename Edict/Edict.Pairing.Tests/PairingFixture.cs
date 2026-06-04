using System.Collections.Concurrent;

using Edict.Contracts.Sending;

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

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();
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
