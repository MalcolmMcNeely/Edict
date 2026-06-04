using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.Outbox;

[Collection(PostgresPersistenceStateWriteFaultCollection.Name)]
public sealed class StateWriteFaultTests(PostgresPersistenceStateWriteFaultFixture fixture)
    : StateWriteFaultScenarios<PostgresPersistenceStateWriteFaultFixture>(fixture);
