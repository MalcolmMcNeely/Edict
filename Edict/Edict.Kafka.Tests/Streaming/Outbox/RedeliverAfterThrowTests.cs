using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Outbox;

[Collection(KafkaStreamingCollection.Name)]
public sealed class RedeliverAfterThrowTests : RedeliverAfterThrowScenarios<KafkaStreamingFixture>
{
    public RedeliverAfterThrowTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
