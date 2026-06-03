using Confluent.Kafka;

using Edict.Telemetry;

using Xunit;

using ErrorCode = Confluent.Kafka.ErrorCode;

namespace Edict.Kafka.Tests.DeadLetter;

// Pinned against the real Confluent.Kafka driver type, not a synthetic — the
// whole point of moving the match out of Core is that this test project can
// reference Confluent.Kafka and so prove the classifier recognises the actual
// exceptions the driver throws (ProduceException derives from KafkaException,
// so the base arm covers a publish fault).
public sealed class KafkaDeadLetterFaultClassifierTests
{
    [Fact]
    public void Classify_ShouldMapKafkaException_ToSubstrate()
    {
        var classifier = new KafkaDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new KafkaException(ErrorCode.Local_Transport));

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldMapProduceException_ToSubstrate()
    {
        var classifier = new KafkaDeadLetterFaultClassifier();

        var exception = new ProduceException<string, string>(
            new Error(ErrorCode.BrokerNotAvailable, "broker outage"),
            new DeliveryResult<string, string>());

        var bucket = classifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldDefer_ForUnrelatedException()
    {
        var classifier = new KafkaDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new InvalidOperationException("not a driver fault"));

        Assert.Null(bucket);
    }
}
