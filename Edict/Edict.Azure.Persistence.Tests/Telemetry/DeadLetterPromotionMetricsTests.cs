using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Telemetry;

[Collection(AzurePersistenceDeadLetterCollection.Name)]
public sealed class DeadLetterPromotionMetricsTests(AzurePersistenceDeadLetterFixture fixture)
    : DeadLetterPromotionMetricsScenarios<AzurePersistenceDeadLetterFixture>(fixture);
