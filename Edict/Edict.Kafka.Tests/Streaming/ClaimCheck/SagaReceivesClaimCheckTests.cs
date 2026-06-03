using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.ClaimCheck;

[Collection(KafkaClaimCheckStreamingCollection.Name)]
public sealed class SagaReceivesClaimCheckTests : SagaReceivesClaimCheckScenarios<KafkaClaimCheckStreamingFixture>
{
    public SagaReceivesClaimCheckTests(KafkaClaimCheckStreamingFixture fixture) : base(fixture) { }
}
