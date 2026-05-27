using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Kafka.Tests.EventHandler;

[Collection(KafkaClusterCollection.Name)]
public sealed class EventHandlerDedupsWithinRingKafkaTests
    : EventHandlerDedupsWithinRingScenarios<KafkaClusterFixture>
{
    public EventHandlerDedupsWithinRingKafkaTests(KafkaClusterFixture fixture)
        : base(fixture)
    {
    }
}
