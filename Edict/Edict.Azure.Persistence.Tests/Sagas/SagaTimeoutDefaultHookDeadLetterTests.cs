using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Sagas;

[Collection(AzurePersistenceSagaTimeoutCollection.Name)]
public sealed class SagaTimeoutDefaultHookDeadLetterTests(AzurePersistenceSagaTimeoutFixture fixture)
    : SagaTimeoutDefaultHookDeadLetterScenarios<AzurePersistenceSagaTimeoutFixture>(fixture);
