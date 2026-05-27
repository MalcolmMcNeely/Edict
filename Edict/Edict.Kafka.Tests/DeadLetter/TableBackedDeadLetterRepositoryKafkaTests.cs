using Edict.Tests.Conformance.DeadLetter;

using Xunit;

namespace Edict.Kafka.Tests.DeadLetter;

[Collection(KafkaClusterCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryKafkaTests(KafkaClusterFixture fixture)
    : TableBackedDeadLetterRepositoryScenarios<KafkaClusterFixture>(fixture);
