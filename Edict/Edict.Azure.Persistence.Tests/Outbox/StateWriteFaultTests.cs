using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Outbox;

[Collection(AzurePersistenceStateWriteFaultCollection.Name)]
public sealed class StateWriteFaultTests(AzurePersistenceStateWriteFaultFixture fixture)
    : StateWriteFaultScenarios<AzurePersistenceStateWriteFaultFixture>(fixture);
