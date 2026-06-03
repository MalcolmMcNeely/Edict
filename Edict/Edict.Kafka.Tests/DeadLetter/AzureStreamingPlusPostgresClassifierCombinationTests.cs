using Azure;

using Edict.Azure.Streaming;
using Edict.Core.DeadLetter;
using Edict.Postgres;
using Edict.Telemetry;

using Npgsql;

using Xunit;

namespace Edict.Kafka.Tests.DeadLetter;

// Cross-substrate combination: an Azure-streaming + Postgres-persistence silo
// registers both real classifiers in one enumerable. Neither driver fault may
// land at Unhandled — the Azure RequestFailedException and the Postgres
// NpgsqlException must each resolve to Substrate through the composition,
// independent of which classifier sits first. Lives here because this is the
// cross-substrate integration project that already references every provider.
public sealed class AzureStreamingPlusPostgresClassifierCombinationTests
{
    [Fact]
    public void Classify_WithBothProviderClassifiers_MapsEachDriverFault_ToSubstrate()
    {
        IDeadLetterFaultClassifier[] registered =
        [
            new AzureStreamingDeadLetterFaultClassifier(),
            new PostgresDeadLetterFaultClassifier(),
        ];

        var azureBucket = DeadLetterFailureClassifier.Classify(new RequestFailedException(503, "queue down"), registered);
        var postgresBucket = DeadLetterFailureClassifier.Classify(new NpgsqlException("connection reset"), registered);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, azureBucket);
        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, postgresBucket);
    }
}
