using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.DeadLetter;

[Collection(AzurePersistenceDeadLetterCollection.Name)]
public sealed class UnregisteredTypePromotesToDeadLetterTests(AzurePersistenceDeadLetterFixture fixture)
    : UnregisteredTypePromotesToDeadLetterScenarios<AzurePersistenceDeadLetterFixture>(fixture);
