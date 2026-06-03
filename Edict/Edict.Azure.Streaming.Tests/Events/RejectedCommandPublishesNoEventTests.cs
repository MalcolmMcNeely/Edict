using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Events;

[Collection(AqsStreamingCollection.Name)]
public sealed class RejectedCommandPublishesNoEventTests : RejectedCommandPublishesNoEventScenarios<AqsStreamingFixture>
{
    public RejectedCommandPublishesNoEventTests(AqsStreamingFixture fixture) : base(fixture) { }
}
