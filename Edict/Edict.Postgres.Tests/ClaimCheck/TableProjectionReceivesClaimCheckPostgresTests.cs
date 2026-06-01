using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresClaimCheckCollection.Name)]
public sealed class TableProjectionReceivesClaimCheckPostgresTests(PostgresClaimCheckClusterFixture fixture)
    : TableProjectionReceivesClaimCheckScenarios<PostgresClaimCheckClusterFixture>(fixture);
