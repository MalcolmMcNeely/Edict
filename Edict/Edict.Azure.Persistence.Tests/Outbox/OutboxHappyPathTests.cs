using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Outbox;

[Collection(AzurePersistenceCollection.Name)]
public sealed class OutboxHappyPathTests(AzurePersistenceFixture fixture)
    : OutboxHappyPathScenarios<AzurePersistenceFixture>(fixture);
