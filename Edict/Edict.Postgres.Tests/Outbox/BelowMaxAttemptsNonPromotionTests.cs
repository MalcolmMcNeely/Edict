using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresPersistenceOutboxFaultGranularityCollection.Name)]
public sealed class BelowMaxAttemptsNonPromotionTests(PostgresPersistenceOutboxFaultGranularityFixture fixture)
    : BelowMaxAttemptsNonPromotionScenarios<PostgresPersistenceOutboxFaultGranularityFixture>(fixture);
