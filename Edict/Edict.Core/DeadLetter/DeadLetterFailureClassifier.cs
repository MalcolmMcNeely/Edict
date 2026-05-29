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
            _ when ContainsSaturated(exception.GetType().Name) =>
                SemanticConventions.DeadLetter.Tags.FailureReasonValues.Saturated,
            _ => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled,
        };
    }

    // Forward-compatibility hook for EdictOutboxSaturatedException. Match by
    // name so the classifier doesn't need a hard reference to a type that
    // doesn't ship yet.
    static bool ContainsSaturated(string typeName) =>
        typeName.Contains("Saturated", StringComparison.OrdinalIgnoreCase);
}
