using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Kafka.Tests.ClaimCheck;

[Collection(KafkaClaimCheckCollection.Name)]
public sealed class TableProjectionReceivesClaimCheckKafkaTests(KafkaClaimCheckClusterFixture fixture)
    : TableProjectionReceivesClaimCheckScenarios<KafkaClaimCheckClusterFixture>(fixture);
