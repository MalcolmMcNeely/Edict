using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Sagas;

[Collection(KafkaSagaTimeoutStreamingCollection.Name)]
public sealed class SagaTimeoutCapCompensationTests : SagaTimeoutCapCompensationScenarios<KafkaSagaTimeoutStreamingFixture>
{
    public SagaTimeoutCapCompensationTests(KafkaSagaTimeoutStreamingFixture fixture) : base(fixture) { }
}
