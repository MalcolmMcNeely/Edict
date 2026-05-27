using Edict.Tests.Conformance.Events;

namespace Edict.Postgres.Tests.Events;

[Collection(PostgresClusterCollection.Name)]
public sealed class RejectedCommandPublishesNoEventPostgresTests(PostgresClusterFixture fixture)
    : RejectedCommandPublishesNoEventScenarios<PostgresClusterFixture>(fixture);
