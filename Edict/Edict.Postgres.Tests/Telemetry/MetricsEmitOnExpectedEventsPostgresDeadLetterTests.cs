using Edict.Postgres.Tests.Outbox;
using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Postgres.Tests.Telemetry;

[Collection(PostgresOutboxControllableExecutorCollection.Name)]
public sealed class MetricsEmitOnExpectedEventsPostgresDeadLetterTests(PostgresOutboxControllableExecutorFixture fixture)
    : DeadLetterPromotionMetricsScenarios<PostgresOutboxControllableExecutorFixture>(fixture);
