using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Events;

[Collection(AqsStreamingCollection.Name)]
public sealed class AcceptedCommandPublishesEventTests : AcceptedCommandPublishesEventScenarios<AqsStreamingFixture>
{
    public AcceptedCommandPublishesEventTests(AqsStreamingFixture fixture) : base(fixture) { }
}
