using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Kafka.Tests.Sagas;

[Collection(KafkaAzureClusterCollection.Name)]
public sealed class SagaSendCommandEffectDeliversKafkaAzureTests(KafkaAzureClusterFixture fixture)
    : SagaSendCommandEffectDeliversScenarios<KafkaAzureClusterFixture>(fixture);
