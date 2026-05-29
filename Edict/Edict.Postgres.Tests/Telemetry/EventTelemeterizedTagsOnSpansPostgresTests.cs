using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Postgres.Tests.Telemetry;

[Collection(PostgresClusterCollection.Name)]
public sealed class EventTelemeterizedTagsOnSpansPostgresTests(PostgresClusterFixture fixture)
    : EventTelemeterizedTagsOnSpansScenarios<PostgresClusterFixture>(fixture);
