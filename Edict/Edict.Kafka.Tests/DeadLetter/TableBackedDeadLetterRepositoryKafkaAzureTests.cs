using Edict.Tests.Conformance.DeadLetter;

using Xunit;

namespace Edict.Kafka.Tests.DeadLetter;

[Collection(KafkaAzureClusterCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryKafkaAzureTests(KafkaAzureClusterFixture fixture)
    : TableBackedDeadLetterRepositoryScenarios<KafkaAzureClusterFixture>(fixture);
