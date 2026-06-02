using Edict.Tests.Conformance.EventHandler;

namespace Edict.Kafka.Tests.EventHandler;

[Collection(KafkaClusterCollection.Name)]
public sealed class EventHandlerNoOpForUnhandledTypeKafkaTests(KafkaClusterFixture fixture)
    : EventHandlerNoOpForUnhandledTypeScenarios<KafkaClusterFixture>(fixture);
