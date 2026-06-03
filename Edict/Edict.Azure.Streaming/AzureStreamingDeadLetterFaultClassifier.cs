using Azure;

using Edict.Core.DeadLetter;
using Edict.Telemetry;

namespace Edict.Azure.Streaming;

// Recognises Azure storage faults by their real type so the dead-letter RCA
// dimension buckets a Queue-publish or Blob-claim-check failure as Substrate
// rather than the catch-all Unhandled. RequestFailedException derives straight
// from Exception, so Core never matched it. The persistence assembly ships its
// own classifier for the same type; when both are wired the enumerable holds
// two agreeing entries (first non-null wins).
sealed class AzureStreamingDeadLetterFaultClassifier : IDeadLetterFaultClassifier
{
    public string? Classify(Exception exception) => exception switch
    {
        RequestFailedException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
        _ => null,
    };
}
