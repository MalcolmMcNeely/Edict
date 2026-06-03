using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Telemetry;

[Collection(AqsStreamingCollection.Name)]
public sealed class EventTelemeterizedTagsOnSpansTests : EventTelemeterizedTagsOnSpansScenarios<AqsStreamingFixture>
{
    public EventTelemeterizedTagsOnSpansTests(AqsStreamingFixture fixture) : base(fixture) { }
}
