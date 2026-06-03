using Azure;

using Edict.Azure.Persistence;
using Edict.Telemetry;

using Xunit;

namespace Edict.Azure.Tests.DeadLetter;

// Pinned against the real Azure.RequestFailedException, not a synthetic — the
// persistence side raises it from Table writes. Azure ships two classifiers
// because Streaming and Persistence are independent assemblies with no shared
// home; both map RequestFailedException to Substrate and agree when wired
// together (first non-null wins).
public sealed class AzurePersistenceDeadLetterFaultClassifierTests
{
    [Fact]
    public void Classify_ShouldMapRequestFailedException_ToSubstrate()
    {
        var classifier = new AzurePersistenceDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new RequestFailedException(500, "table write failed"));

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldDefer_ForUnrelatedException()
    {
        var classifier = new AzurePersistenceDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new InvalidOperationException("not a driver fault"));

        Assert.Null(bucket);
    }
}
