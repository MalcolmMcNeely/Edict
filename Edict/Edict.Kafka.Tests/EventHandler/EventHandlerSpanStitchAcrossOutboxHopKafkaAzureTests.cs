using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Kafka.Tests.EventHandler;

[Collection(KafkaAzureClusterCollection.Name)]
public sealed class EventHandlerSpanStitchAcrossOutboxHopKafkaAzureTests(KafkaAzureClusterFixture fixture)
    : EventHandlerSpanStitchAcrossOutboxHopScenarios<KafkaAzureClusterFixture>(fixture);
