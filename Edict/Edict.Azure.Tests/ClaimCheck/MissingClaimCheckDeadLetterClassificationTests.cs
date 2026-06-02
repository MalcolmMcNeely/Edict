using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureBlobMissingDeadLetterCollection.Name)]
public sealed class MissingClaimCheckDeadLetterClassificationTests(AzureBlobMissingDeadLetterClusterFixture fixture)
    : MissingClaimCheckDeadLetterClassificationScenarios<AzureBlobMissingDeadLetterClusterFixture>(fixture);
