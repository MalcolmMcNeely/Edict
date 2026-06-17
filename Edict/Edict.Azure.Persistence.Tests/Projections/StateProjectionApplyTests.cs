using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Projections;

[Collection(AzurePersistenceCollection.Name)]
public sealed class StateProjectionApplyTests(AzurePersistenceFixture fixture)
    : StateProjectionApplyScenarios<AzurePersistenceFixture>(fixture);
