using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresClaimCheckCollection.Name)]
public sealed class SagaReceivesClaimCheckPostgresTests(PostgresClaimCheckClusterFixture fixture)
    : SagaReceivesClaimCheckScenarios<PostgresClaimCheckClusterFixture>(fixture);
