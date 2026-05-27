using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Kafka.Tests.Sagas;

[Collection(KafkaClusterCollection.Name)]
public sealed class SagaSendCommandEffectDeliversKafkaTests(KafkaClusterFixture fixture)
    : SagaSendCommandEffectDeliversScenarios<KafkaClusterFixture>(fixture);
