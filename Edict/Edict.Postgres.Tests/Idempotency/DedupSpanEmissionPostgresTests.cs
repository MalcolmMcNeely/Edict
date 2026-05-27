using Edict.Tests.Conformance.Idempotency;

namespace Edict.Postgres.Tests.Idempotency;

[Collection(PostgresClusterCollection.Name)]
public sealed class DedupSpanEmissionPostgresTests(PostgresClusterFixture fixture)
    : DedupSpanEmissionScenarios<PostgresClusterFixture>(fixture);
