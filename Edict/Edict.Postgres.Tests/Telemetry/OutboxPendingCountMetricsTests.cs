using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Telemetry;

[Collection(PostgresPersistenceDeadLetterCollection.Name)]
public sealed class OutboxPendingCountMetricsTests(PostgresPersistenceDeadLetterFixture fixture)
    : OutboxPendingCountMetricsScenarios<PostgresPersistenceDeadLetterFixture>(fixture);
