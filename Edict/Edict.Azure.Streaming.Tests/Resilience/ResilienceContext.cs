using System.Collections.Concurrent;

using Edict.Tests.Conformance.Streaming.References;

namespace Edict.Azure.Streaming.Tests.Resilience;

// The per-fixture context the Orleans silo configurator reads by key: a
// connection string to the fixture-owned Azurite (the broker is the fault point)
// plus the in-memory reference persistence shared across the in-process silos
// (the faked axis — never asserted on, except the projection row the silo-kill
// proof reads back from the reference store after redelivery).
sealed record ResilienceContext(
    string ConnectionString,
    ReferenceTableStoreFactory TableStoreFactory,
    ReferenceClaimCheckStore ClaimCheckStore);

static class ResilienceContextRegistry
{
    public const string ContextKeyProperty = "ResilienceContextKey";

    static readonly ConcurrentDictionary<string, ResilienceContext> _contexts = new();

    public static string Register(ResilienceContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _contexts[key] = context;
        return key;
    }

    public static ResilienceContext Get(string key) => _contexts[key];

    public static void Unregister(string key) => _contexts.TryRemove(key, out _);
}
