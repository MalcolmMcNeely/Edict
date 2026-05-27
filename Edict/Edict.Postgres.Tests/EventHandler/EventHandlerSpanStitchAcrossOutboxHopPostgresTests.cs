using Edict.Tests.Conformance.EventHandler;

namespace Edict.Postgres.Tests.EventHandler;

[Collection(PostgresClusterCollection.Name)]
public sealed class EventHandlerSpanStitchAcrossOutboxHopPostgresTests(PostgresClusterFixture fixture)
    : EventHandlerSpanStitchAcrossOutboxHopScenarios<PostgresClusterFixture>(fixture);
