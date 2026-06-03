using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.ClaimCheck;

[Collection(AzurePersistenceBlobMissingCollection.Name)]
public sealed class MissingClaimCheckDeadLetterClassificationTests(AzurePersistenceBlobMissingFixture fixture)
    : MissingClaimCheckDeadLetterClassificationScenarios<AzurePersistenceBlobMissingFixture>(fixture);
