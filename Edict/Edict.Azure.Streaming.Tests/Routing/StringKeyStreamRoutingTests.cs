using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Routing;

[Collection(AqsStreamingCollection.Name)]
public sealed class StringKeyStreamRoutingTests : StringKeyStreamRoutingScenarios<AqsStreamingFixture>
{
    public StringKeyStreamRoutingTests(AqsStreamingFixture fixture) : base(fixture) { }
}
