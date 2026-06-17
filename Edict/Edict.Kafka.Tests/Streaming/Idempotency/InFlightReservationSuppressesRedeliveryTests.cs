using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Idempotency;

[Collection(KafkaStreamingCollection.Name)]
public sealed class InFlightReservationSuppressesRedeliveryTests
    : InFlightReservationSuppressesRedeliveryScenarios<KafkaStreamingFixture>
{
    public InFlightReservationSuppressesRedeliveryTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
