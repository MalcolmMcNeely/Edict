using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresClaimCheckCollection.Name)]
public sealed class LargePayloadPublishesViaBlobPostgresTests(PostgresClaimCheckClusterFixture fixture)
    : LargePayloadPublishesViaBlobScenarios<PostgresClaimCheckClusterFixture>(fixture);
