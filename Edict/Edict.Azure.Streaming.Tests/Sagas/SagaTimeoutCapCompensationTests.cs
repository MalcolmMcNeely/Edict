using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Sagas;

[Collection(AqsSagaTimeoutStreamingCollection.Name)]
public sealed class SagaTimeoutCapCompensationTests : SagaTimeoutCapCompensationScenarios<AqsSagaTimeoutStreamingFixture>
{
    public SagaTimeoutCapCompensationTests(AqsSagaTimeoutStreamingFixture fixture) : base(fixture) { }
}
