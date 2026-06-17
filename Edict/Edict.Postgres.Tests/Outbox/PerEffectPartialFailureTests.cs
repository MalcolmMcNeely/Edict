using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresPersistenceOutboxFaultGranularityCollection.Name)]
public sealed class PerEffectPartialFailureTests(PostgresPersistenceOutboxFaultGranularityFixture fixture)
    : PerEffectPartialFailureScenarios<PostgresPersistenceOutboxFaultGranularityFixture>(fixture);
