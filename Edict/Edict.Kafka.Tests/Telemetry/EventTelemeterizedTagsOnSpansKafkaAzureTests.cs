using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Kafka.Tests.Telemetry;

[Collection(KafkaAzureClusterCollection.Name)]
public sealed class EventTelemeterizedTagsOnSpansKafkaAzureTests(KafkaAzureClusterFixture fixture)
    : EventTelemeterizedTagsOnSpansScenarios<KafkaAzureClusterFixture>(fixture);
