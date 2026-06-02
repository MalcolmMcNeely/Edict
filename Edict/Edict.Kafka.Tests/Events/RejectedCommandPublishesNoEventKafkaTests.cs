using Edict.Tests.Conformance.Events;

namespace Edict.Kafka.Tests.Events;

[Collection(KafkaClusterCollection.Name)]
public sealed class RejectedCommandPublishesNoEventKafkaTests(KafkaClusterFixture fixture)
    : RejectedCommandPublishesNoEventScenarios<KafkaClusterFixture>(fixture);
