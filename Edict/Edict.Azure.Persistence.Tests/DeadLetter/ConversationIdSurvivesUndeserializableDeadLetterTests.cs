using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.DeadLetter;

[Collection(AzurePersistenceDeadLetterDegradeCollection.Name)]
public sealed class ConversationIdSurvivesUndeserializableDeadLetterTests(AzurePersistenceDeadLetterDegradeFixture fixture)
    : ConversationIdSurvivesUndeserializableDeadLetterScenarios<AzurePersistenceDeadLetterDegradeFixture>(fixture);
