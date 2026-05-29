using Edict.Postgres.Tests.ClaimCheck;
using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Postgres.Tests.Telemetry;

[Collection(PostgresClaimCheckCollection.Name)]
public sealed class MetricsEmitOnExpectedEventsPostgresClaimCheckTests(PostgresClaimCheckClusterFixture fixture)
    : ClaimCheckPayloadSizeMetricsScenarios<PostgresClaimCheckClusterFixture>(fixture);
