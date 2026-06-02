using Edict.Tests.Conformance.EventHandler;

namespace Edict.Kafka.Tests.EventHandler;

[Collection(KafkaClusterCollection.Name)]
public sealed class EventHandlerHandlesPublishedEventKafkaTests(KafkaClusterFixture fixture)
    : EventHandlerHandlesPublishedEventScenarios<KafkaClusterFixture>(fixture);
