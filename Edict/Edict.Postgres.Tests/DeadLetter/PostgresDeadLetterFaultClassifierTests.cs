using Edict.Telemetry;

using Npgsql;

using Xunit;

namespace Edict.Postgres.Tests.DeadLetter;

// Pinned against the real Npgsql driver types, not synthetics — the whole
// point of moving the match out of Core is that this test project can
// reference Npgsql and so prove the classifier recognises the actual
// exceptions the driver throws.
public sealed class PostgresDeadLetterFaultClassifierTests
{
    [Fact]
    public void Classify_ShouldMapNpgsqlException_ToSubstrate()
    {
        var classifier = new PostgresDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new NpgsqlException("connection reset"));

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldMapPostgresException_ToSubstrate()
    {
        var classifier = new PostgresDeadLetterFaultClassifier();

        // 53300 = too_many_connections (pool exhaustion under saturation).
        var exception = new PostgresException("too many clients", "FATAL", "FATAL", "53300");

        var bucket = classifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldMapEdictPostgresStorageException_ToSubstrate()
    {
        var classifier = new PostgresDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new EdictPostgresStorageException("WriteStateAsync fault"));

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldDefer_ForUnrelatedException()
    {
        var classifier = new PostgresDeadLetterFaultClassifier();

        var bucket = classifier.Classify(new InvalidOperationException("not a driver fault"));

        Assert.Null(bucket);
    }
}
