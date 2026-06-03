using Confluent.Kafka;

using Edict.Core.DeadLetter;
using Edict.Telemetry;

namespace Edict.Kafka;

// Recognises Kafka driver faults by their real type so the dead-letter RCA
// dimension buckets a broker outage or exhausted publish retries as Substrate
// rather than the catch-all Unhandled. ProduceException derives from
// KafkaException, so the base arm covers a publish fault.
sealed class KafkaDeadLetterFaultClassifier : IDeadLetterFaultClassifier
{
    public string? Classify(Exception exception) => exception switch
    {
        KafkaException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
        _ => null,
    };
}
