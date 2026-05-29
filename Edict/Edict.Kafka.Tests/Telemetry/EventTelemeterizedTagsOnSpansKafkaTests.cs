using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Kafka.Tests.Telemetry;

[Collection(KafkaClusterCollection.Name)]
public sealed class EventTelemeterizedTagsOnSpansKafkaTests(KafkaClusterFixture fixture)
    : EventTelemeterizedTagsOnSpansScenarios<KafkaClusterFixture>(fixture);
