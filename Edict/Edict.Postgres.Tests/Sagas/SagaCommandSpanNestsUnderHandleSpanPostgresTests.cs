using Edict.Tests.Conformance.Sagas;

namespace Edict.Postgres.Tests.Sagas;

[Collection(PostgresClusterCollection.Name)]
public sealed class SagaCommandSpanNestsUnderHandleSpanPostgresTests(PostgresClusterFixture fixture)
    : SagaCommandSpanNestsUnderHandleSpanScenarios<PostgresClusterFixture>(fixture);
