using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Projections;

[Collection(AzurePersistenceCollection.Name)]
public sealed class TableProjectionSingletonTests(AzurePersistenceFixture fixture)
    : TableProjectionSingletonScenarios<AzurePersistenceFixture>(fixture);
