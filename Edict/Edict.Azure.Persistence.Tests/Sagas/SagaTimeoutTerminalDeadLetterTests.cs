using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Sagas;

[Collection(AzurePersistenceCollection.Name)]
public sealed class SagaTimeoutTerminalDeadLetterTests(AzurePersistenceFixture fixture)
    : SagaTimeoutTerminalDeadLetterScenarios<AzurePersistenceFixture>(fixture);
