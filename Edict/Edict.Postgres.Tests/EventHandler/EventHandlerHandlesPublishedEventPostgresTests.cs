using Edict.Tests.Conformance.EventHandler;

namespace Edict.Postgres.Tests.EventHandler;

[Collection(PostgresClusterCollection.Name)]
public sealed class EventHandlerHandlesPublishedEventPostgresTests(PostgresClusterFixture fixture)
    : EventHandlerHandlesPublishedEventScenarios<PostgresClusterFixture>(fixture);
