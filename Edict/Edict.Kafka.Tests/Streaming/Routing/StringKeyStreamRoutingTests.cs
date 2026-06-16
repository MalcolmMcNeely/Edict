using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Routing;

[Collection(KafkaStreamingCollection.Name)]
public sealed class StringKeyStreamRoutingTests : StringKeyStreamRoutingScenarios<KafkaStreamingFixture>
{
    public StringKeyStreamRoutingTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
