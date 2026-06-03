using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.ClaimCheck;

[Collection(KafkaClaimCheckStreamingCollection.Name)]
public sealed class LargePayloadPublishesViaBlobTests : LargePayloadPublishesViaBlobScenarios<KafkaClaimCheckStreamingFixture>
{
    public LargePayloadPublishesViaBlobTests(KafkaClaimCheckStreamingFixture fixture) : base(fixture) { }
}
