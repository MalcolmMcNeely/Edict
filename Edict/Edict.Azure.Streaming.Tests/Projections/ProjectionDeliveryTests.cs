using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Projections;

[Collection(AqsStreamingCollection.Name)]
public sealed class ProjectionDeliveryTests : ProjectionDeliveryScenarios<AqsStreamingFixture>
{
    public ProjectionDeliveryTests(AqsStreamingFixture fixture) : base(fixture) { }
}
