using Edict.Tests.Conformance.Commands;

namespace Edict.Postgres.Tests.Commands;

[Collection(PostgresClusterCollection.Name)]
public sealed class CommandValidatorPostgresTests(PostgresClusterFixture fixture)
    : CommandValidatorScenarios<PostgresClusterFixture>(fixture);
