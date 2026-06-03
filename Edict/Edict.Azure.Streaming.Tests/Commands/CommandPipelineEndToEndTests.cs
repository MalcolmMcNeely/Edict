using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Commands;

[Collection(AqsStreamingCollection.Name)]
public sealed class CommandPipelineEndToEndTests : CommandPipelineEndToEndScenarios<AqsStreamingFixture>
{
    public CommandPipelineEndToEndTests(AqsStreamingFixture fixture) : base(fixture) { }
}
