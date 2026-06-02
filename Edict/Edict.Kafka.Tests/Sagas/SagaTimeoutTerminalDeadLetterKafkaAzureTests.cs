using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Kafka.Tests.Sagas;

[Collection(KafkaAzureClusterCollection.Name)]
public sealed class SagaTimeoutTerminalDeadLetterKafkaAzureTests(KafkaAzureClusterFixture fixture)
    : SagaTimeoutTerminalDeadLetterScenarios<KafkaAzureClusterFixture>(fixture);
