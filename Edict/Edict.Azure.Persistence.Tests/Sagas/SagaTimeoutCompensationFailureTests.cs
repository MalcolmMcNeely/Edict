using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Sagas;

[Collection(AzurePersistenceSagaTimeoutCollection.Name)]
public sealed class SagaTimeoutCompensationFailureTests(AzurePersistenceSagaTimeoutFixture fixture)
    : SagaTimeoutCompensationFailureScenarios<AzurePersistenceSagaTimeoutFixture>(fixture);
