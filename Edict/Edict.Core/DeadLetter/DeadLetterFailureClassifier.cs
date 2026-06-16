using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.Json;

using Edict.Contracts.ClaimCheck;
using Edict.Telemetry;

namespace Edict.Core.DeadLetter;

static class DeadLetterFailureClassifier
{
    // Composition overload. The built-in switch is privileged: it runs first
    // and a non-Unhandled result always wins, so no provider classifier can
    // re-bucket a framework cause. Only where the built-in defers (Unhandled)
    // are the registered classifiers consulted, in registration order, first
    // non-null winning. Each call is individually wrapped so a throwing
    // classifier degrades to a defer and consultation continues: Promote()
    // runs outside the engine's per-group catch and must never throw.
    public static string Classify(Exception exception, IEnumerable<IDeadLetterFaultClassifier> registeredClassifiers)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(registeredClassifiers);

        var builtIn = Classify(exception);
        if (builtIn != SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled)
        {
            return builtIn;
        }

        foreach (var classifier in registeredClassifiers)
        {
            string? bucket;
            try
            {
                bucket = classifier.Classify(exception);
            }
            catch
            {
                continue;
            }
            if (bucket is not null)
            {
                return bucket;
            }
        }

        return SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled;
    }

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
            EdictClaimCheckFetchException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            EdictSagaCoordinationException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.ConsumerBug,
            EdictSagaCompensationException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.ConsumerBug,
            EdictSagaTimeoutException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTimeout,
            EdictSagaTerminalException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTerminal,
            EdictInternalInvariantException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.InternalBug,
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
