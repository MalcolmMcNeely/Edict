using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Idempotency;

[Collection(AzurePersistenceCollection.Name)]
public sealed class RingSurvivesDeactivationTests(AzurePersistenceFixture fixture)
    : RingSurvivesDeactivationScenarios<AzurePersistenceFixture>(fixture);
