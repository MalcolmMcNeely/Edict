using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Kafka.Tests.EventHandler;

[Collection(KafkaClusterCollection.Name)]
public sealed class EventHandlerSpanStitchAcrossOutboxHopKafkaTests(KafkaClusterFixture fixture)
    : EventHandlerSpanStitchAcrossOutboxHopScenarios<KafkaClusterFixture>(fixture);
