using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.DeadLetter;

[Collection(PostgresPersistenceDeadLetterCollection.Name)]
public sealed class UnregisteredTypePromotesToDeadLetterTests(PostgresPersistenceDeadLetterFixture fixture)
    : UnregisteredTypePromotesToDeadLetterScenarios<PostgresPersistenceDeadLetterFixture>(fixture);
