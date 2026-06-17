using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Outbox;

[Collection(AzurePersistenceOutboxFaultGranularityCollection.Name)]
public sealed class BelowMaxAttemptsNonPromotionTests(AzurePersistenceOutboxFaultGranularityFixture fixture)
    : BelowMaxAttemptsNonPromotionScenarios<AzurePersistenceOutboxFaultGranularityFixture>(fixture);
