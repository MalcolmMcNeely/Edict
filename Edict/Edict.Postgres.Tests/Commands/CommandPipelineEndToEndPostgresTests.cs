using Edict.Tests.Conformance.Commands;

namespace Edict.Postgres.Tests.Commands;

[Collection(PostgresClusterCollection.Name)]
public sealed class CommandPipelineEndToEndPostgresTests(PostgresClusterFixture fixture)
    : CommandPipelineEndToEndScenarios<PostgresClusterFixture>(fixture);
