using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Kafka.Tests.ClaimCheck;

[Collection(KafkaClaimCheckCollection.Name)]
public sealed class SagaReceivesClaimCheckKafkaTests(KafkaClaimCheckClusterFixture fixture)
    : SagaReceivesClaimCheckScenarios<KafkaClaimCheckClusterFixture>(fixture);
