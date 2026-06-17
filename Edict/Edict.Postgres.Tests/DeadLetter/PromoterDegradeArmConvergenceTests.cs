using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.DeadLetter;

[Collection(PostgresPersistenceDeadLetterDegradeCollection.Name)]
public sealed class PromoterDegradeArmConvergenceTests(PostgresPersistenceDeadLetterDegradeFixture fixture)
    : PromoterDegradeArmConvergenceScenarios<PostgresPersistenceDeadLetterDegradeFixture>(fixture);
