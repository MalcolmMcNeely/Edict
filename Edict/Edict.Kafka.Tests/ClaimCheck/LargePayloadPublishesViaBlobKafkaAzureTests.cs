using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Kafka.Tests.ClaimCheck;

[Collection(KafkaBlobClaimCheckCollection.Name)]
public sealed class LargePayloadPublishesViaBlobKafkaAzureTests(KafkaBlobClaimCheckClusterFixture fixture)
    : LargePayloadPublishesViaBlobScenarios<KafkaBlobClaimCheckClusterFixture>(fixture);
