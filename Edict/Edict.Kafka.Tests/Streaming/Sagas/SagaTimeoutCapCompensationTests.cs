using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Sagas;

[Collection(KafkaStreamingCollection.Name)]
public sealed class SagaTimeoutCapCompensationTests : SagaTimeoutCapCompensationScenarios<KafkaStreamingFixture>
{
    public SagaTimeoutCapCompensationTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
