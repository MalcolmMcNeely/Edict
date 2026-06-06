using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Projections;

[Collection(AqsStreamingCollection.Name)]
public sealed class ProjectionDeliverySerializationTests : ProjectionDeliverySerializationScenarios<AqsStreamingFixture>
{
    public ProjectionDeliverySerializationTests(AqsStreamingFixture fixture) : base(fixture) { }
}
