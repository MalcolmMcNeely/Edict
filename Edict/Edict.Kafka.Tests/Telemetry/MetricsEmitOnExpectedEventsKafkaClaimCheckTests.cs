using Edict.Kafka.Tests.ClaimCheck;
using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Kafka.Tests.Telemetry;

[Collection(KafkaClaimCheckCollection.Name)]
public sealed class MetricsEmitOnExpectedEventsKafkaClaimCheckTests(KafkaClaimCheckClusterFixture fixture)
    : ClaimCheckPayloadSizeMetricsScenarios<KafkaClaimCheckClusterFixture>(fixture);
