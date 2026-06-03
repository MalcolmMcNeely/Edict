using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Sagas;

[Collection(AqsStreamingCollection.Name)]
public sealed class SagaSendCommandEffectDeliversTests : SagaSendCommandEffectDeliversScenarios<AqsStreamingFixture>
{
    public SagaSendCommandEffectDeliversTests(AqsStreamingFixture fixture) : base(fixture) { }
}
