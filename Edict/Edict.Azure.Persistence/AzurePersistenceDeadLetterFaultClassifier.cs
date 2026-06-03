using Azure;

using Edict.Core.DeadLetter;
using Edict.Telemetry;

namespace Edict.Azure.Persistence;

// Recognises Azure storage faults by their real type so the dead-letter RCA
// dimension buckets a Table-write failure as Substrate rather than the
// catch-all Unhandled. RequestFailedException derives straight from Exception,
// so Core never matched it. The streaming assembly ships its own classifier
// for the same type; when both are wired the enumerable holds two agreeing
// entries (first non-null wins).
sealed class AzurePersistenceDeadLetterFaultClassifier : IDeadLetterFaultClassifier
{
    public string? Classify(Exception exception) => exception switch
    {
        RequestFailedException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
        _ => null,
    };
}
