using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Idempotency;

[Collection(AqsStreamingCollection.Name)]
public sealed class InFlightReservationSuppressesRedeliveryTests
    : InFlightReservationSuppressesRedeliveryScenarios<AqsStreamingFixture>
{
    public InFlightReservationSuppressesRedeliveryTests(AqsStreamingFixture fixture) : base(fixture) { }
}
