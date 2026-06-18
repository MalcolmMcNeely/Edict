using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Projections;

[Collection(AqsStreamingCollection.Name)]
public sealed class ProjectionCursorReadOverStreamTests : ProjectionCursorReadOverStreamScenarios<AqsStreamingFixture>
{
    public ProjectionCursorReadOverStreamTests(AqsStreamingFixture fixture) : base(fixture) { }
}
