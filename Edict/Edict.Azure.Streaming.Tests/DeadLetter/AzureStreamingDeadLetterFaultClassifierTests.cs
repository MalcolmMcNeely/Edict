using Azure;

using Edict.Azure.Streaming;
using Edict.Telemetry;

using Xunit;

namespace Edict.Azure.Streaming.Tests.DeadLetter;

// Pinned against the real Azure.RequestFailedException, not a synthetic — the
// streaming side raises it from Queue publish and Blob claim-check, and it
// derives straight from Exception, so Core never matched it. Moving the match
// to where Azure.Core is referenceable proves the classifier recognises the
// actual exception the SDK throws.
public sealed class AzureStreamingDeadLetterFaultClassifierTests
{
    [Fact]
    public void Classify_ShouldMapRequestFailedException_ToSubstrate()
    {
        var classifier = new AzureStreamingDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new RequestFailedException(503, "queue unavailable"));

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldDefer_ForUnrelatedException()
    {
        var classifier = new AzureStreamingDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new InvalidOperationException("not a driver fault"));

        Assert.Null(bucket);
    }
}
