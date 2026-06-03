using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Projections;

[Collection(AqsStreamingCollection.Name)]
public sealed class ProjectionUnhandledEventTests : ProjectionUnhandledEventScenarios<AqsStreamingFixture>
{
    public ProjectionUnhandledEventTests(AqsStreamingFixture fixture) : base(fixture) { }
}
