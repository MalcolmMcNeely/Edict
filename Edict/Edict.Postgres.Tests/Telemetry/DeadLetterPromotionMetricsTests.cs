using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Telemetry;

[Collection(PostgresPersistenceDeadLetterCollection.Name)]
public sealed class DeadLetterPromotionMetricsTests(PostgresPersistenceDeadLetterFixture fixture)
    : DeadLetterPromotionMetricsScenarios<PostgresPersistenceDeadLetterFixture>(fixture);
