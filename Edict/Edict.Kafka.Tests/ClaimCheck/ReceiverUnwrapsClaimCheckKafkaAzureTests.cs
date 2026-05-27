using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Kafka.Tests.ClaimCheck;

[Collection(KafkaBlobClaimCheckCollection.Name)]
public sealed class ReceiverUnwrapsClaimCheckKafkaAzureTests(KafkaBlobClaimCheckClusterFixture fixture)
    : ReceiverUnwrapsClaimCheckScenarios<KafkaBlobClaimCheckClusterFixture>(fixture);
