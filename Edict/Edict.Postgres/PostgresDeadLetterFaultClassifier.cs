using Edict.Core.DeadLetter;
using Edict.Telemetry;

using Npgsql;

namespace Edict.Postgres;

// Recognises Postgres driver faults by their real type so the dead-letter RCA
// dimension buckets a connection drop or pool exhaustion as Substrate rather
// than the catch-all Unhandled. PostgresException derives from NpgsqlException,
// so the base arm covers it; EdictPostgresStorageException is the serializable
// wrapper the provider rethrows across the grain boundary.
sealed class PostgresDeadLetterFaultClassifier : IDeadLetterFaultClassifier
{
    public string? Classify(Exception exception) => exception switch
    {
        NpgsqlException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
        EdictPostgresStorageException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
        _ => null,
    };
}
