using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Kafka.Tests.ClaimCheck;

[Collection(KafkaClaimCheckCollection.Name)]
public sealed class LargePayloadPublishesViaBlobKafkaTests(KafkaClaimCheckClusterFixture fixture)
    : LargePayloadPublishesViaBlobScenarios<KafkaClaimCheckClusterFixture>(fixture);
