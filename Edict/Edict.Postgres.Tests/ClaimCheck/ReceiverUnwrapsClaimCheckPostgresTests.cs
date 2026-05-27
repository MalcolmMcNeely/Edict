using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresClaimCheckCollection.Name)]
public sealed class ReceiverUnwrapsClaimCheckPostgresTests(PostgresClaimCheckClusterFixture fixture)
    : ReceiverUnwrapsClaimCheckScenarios<PostgresClaimCheckClusterFixture>(fixture);
