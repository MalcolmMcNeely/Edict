using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.DeadLetter;

[Collection(AzurePersistenceCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryTests(AzurePersistenceFixture fixture)
    : TableBackedDeadLetterRepositoryScenarios<AzurePersistenceFixture>(fixture);
