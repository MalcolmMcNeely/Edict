using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Kafka.Tests.ClaimCheck;

[Collection(KafkaClaimCheckCollection.Name)]
public sealed class ReceiverUnwrapsClaimCheckKafkaTests(KafkaClaimCheckClusterFixture fixture)
    : ReceiverUnwrapsClaimCheckScenarios<KafkaClaimCheckClusterFixture>(fixture);
