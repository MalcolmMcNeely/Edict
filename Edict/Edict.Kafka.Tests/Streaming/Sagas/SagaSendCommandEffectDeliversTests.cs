using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Sagas;

[Collection(KafkaStreamingCollection.Name)]
public sealed class SagaSendCommandEffectDeliversTests : SagaSendCommandEffectDeliversScenarios<KafkaStreamingFixture>
{
    public SagaSendCommandEffectDeliversTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
