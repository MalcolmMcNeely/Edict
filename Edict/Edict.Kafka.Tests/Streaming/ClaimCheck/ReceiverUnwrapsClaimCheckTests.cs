using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.ClaimCheck;

[Collection(KafkaClaimCheckStreamingCollection.Name)]
public sealed class ReceiverUnwrapsClaimCheckTests : ReceiverUnwrapsClaimCheckScenarios<KafkaClaimCheckStreamingFixture>
{
    public ReceiverUnwrapsClaimCheckTests(KafkaClaimCheckStreamingFixture fixture) : base(fixture) { }
}
