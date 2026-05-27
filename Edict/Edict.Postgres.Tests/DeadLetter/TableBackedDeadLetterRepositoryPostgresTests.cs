using Edict.Tests.Conformance.DeadLetter;

namespace Edict.Postgres.Tests.DeadLetter;

[Collection(PostgresClusterCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryPostgresTests(PostgresClusterFixture fixture)
    : TableBackedDeadLetterRepositoryScenarios<PostgresClusterFixture>(fixture);
