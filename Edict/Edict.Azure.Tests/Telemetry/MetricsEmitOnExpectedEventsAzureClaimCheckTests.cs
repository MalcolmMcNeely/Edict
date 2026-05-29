using Edict.Azure.Tests.ClaimCheck;
using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Azure.Tests.Telemetry;

[Collection(AzureClaimCheckCollection.Name)]
public sealed class MetricsEmitOnExpectedEventsAzureClaimCheckTests(AzureClaimCheckClusterFixture fixture)
    : ClaimCheckPayloadSizeMetricsScenarios<AzureClaimCheckClusterFixture>(fixture);
