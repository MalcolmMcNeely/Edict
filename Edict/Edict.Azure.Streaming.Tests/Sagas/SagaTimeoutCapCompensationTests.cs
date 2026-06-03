using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Sagas;

[Collection(AqsStreamingCollection.Name)]
public sealed class SagaTimeoutCapCompensationTests : SagaTimeoutCapCompensationScenarios<AqsStreamingFixture>
{
    public SagaTimeoutCapCompensationTests(AqsStreamingFixture fixture) : base(fixture) { }
}
