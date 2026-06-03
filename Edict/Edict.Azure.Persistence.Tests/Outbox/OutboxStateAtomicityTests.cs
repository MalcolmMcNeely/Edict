using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Outbox;

[Collection(AzurePersistenceCollection.Name)]
public sealed class OutboxStateAtomicityTests(AzurePersistenceFixture fixture)
    : OutboxStateAtomicityScenarios<AzurePersistenceFixture>(fixture);
