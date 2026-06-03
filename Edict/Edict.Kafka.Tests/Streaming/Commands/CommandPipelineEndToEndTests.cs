using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Commands;

[Collection(KafkaStreamingCollection.Name)]
public sealed class CommandPipelineEndToEndTests : CommandPipelineEndToEndScenarios<KafkaStreamingFixture>
{
    public CommandPipelineEndToEndTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
