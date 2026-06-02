using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.Json;

using Edict.Contracts.ClaimCheck;
using Edict.Telemetry;

namespace Edict.Core.DeadLetter;

static class DeadLetterFailureClassifier
{
    public static string Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            TimeoutException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Timeout,
            OperationCanceledException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Timeout,
            EdictEnvelopeOverflowException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization,
            JsonException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization,
            SerializationException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization,
            HttpRequestException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            SocketException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            IOException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            EdictUnregisteredTypeException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Wiring,
            EdictClaimCheckFetchException { FetchReason: EdictClaimCheckFetchException.Reason.KeyMalformed } =>
                SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization,
            EdictClaimCheckFetchException { FetchReason: EdictClaimCheckFetchException.Reason.PayloadMissing } =>
                SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            EdictSagaCoordinationException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.ConsumerBug,
            EdictSagaTimeoutException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTimeout,
            EdictSagaTerminalException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTerminal,
            EdictInternalInvariantException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.InternalBug,
            _ when ContainsSaturated(exception.GetType().Name) =>
                SemanticConventions.DeadLetter.Tags.FailureReasonValues.Saturated,
            _ when IsPostgresDriverFault(exception.GetType().Name) =>
                SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            _ => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled,
        };
    }

    // Forward-compatibility hook for EdictOutboxSaturatedException. Match by
    // name so the classifier doesn't need a hard reference to a type that
    // doesn't ship yet.
    static bool ContainsSaturated(string typeName) =>
        typeName.Contains("Saturated", StringComparison.OrdinalIgnoreCase);

    // Core cannot reference Npgsql or Edict.Postgres, so a Postgres connection
    // drop or pool exhaustion is recognised by type-name: the raw driver
    // NpgsqlException / PostgresException, and the EdictPostgresStorageException
    // wrapper the provider rethrows so Orleans can serialise it back across the
    // grain boundary. All are substrate faults, not consumer bugs.
    static bool IsPostgresDriverFault(string typeName) =>
        typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
        || typeName.Contains("Postgres", StringComparison.OrdinalIgnoreCase);
}
