using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.DeadLetter;

[Collection(AzurePersistenceDeadLetterCollection.Name)]
public sealed class HandlerFailurePromotesToDeadLetterTests(AzurePersistenceDeadLetterFixture fixture)
    : HandlerFailurePromotesToDeadLetterScenarios<AzurePersistenceDeadLetterFixture>(fixture);
