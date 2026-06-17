using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.DeadLetter;

[Collection(AzurePersistenceDeadLetterDegradeCollection.Name)]
public sealed class PromoterDegradeArmConvergenceTests(AzurePersistenceDeadLetterDegradeFixture fixture)
    : PromoterDegradeArmConvergenceScenarios<AzurePersistenceDeadLetterDegradeFixture>(fixture);
