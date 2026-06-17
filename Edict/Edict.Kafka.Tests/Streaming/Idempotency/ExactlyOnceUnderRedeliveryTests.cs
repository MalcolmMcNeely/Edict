using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Idempotency;

[Collection(KafkaStreamingCollection.Name)]
public sealed class ExactlyOnceUnderRedeliveryTests : ExactlyOnceUnderRedeliveryScenarios<KafkaStreamingFixture>
{
    public ExactlyOnceUnderRedeliveryTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
