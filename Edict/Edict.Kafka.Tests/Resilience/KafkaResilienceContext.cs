using System.Collections.Concurrent;

using Edict.Tests.Conformance.Streaming.References;

namespace Edict.Kafka.Tests.Resilience;

// The per-fixture context the Orleans silo configurator reads by key: the
// fixture-owned Kafka broker coordinates (the broker is the fault / redelivery
// point) plus the in-memory reference persistence shared across the in-process
// silos (the faked axis — never asserted on, except the projection row the
// silo-kill proof reads back from the reference store after redelivery).
sealed record KafkaResilienceContext(
    string BootstrapServers,
    string ConsumerGroup,
    ReferenceTableStoreFactory TableStoreFactory,
    ReferenceClaimCheckStore ClaimCheckStore);

static class KafkaResilienceContextRegistry
{
    public const string ContextKeyProperty = "KafkaResilienceContextKey";

    static readonly ConcurrentDictionary<string, KafkaResilienceContext> _contexts = new();

    public static string Register(KafkaResilienceContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _contexts[key] = context;
        return key;
    }

    public static KafkaResilienceContext Get(string key) => _contexts[key];

    public static void Unregister(string key) => _contexts.TryRemove(key, out _);
}
