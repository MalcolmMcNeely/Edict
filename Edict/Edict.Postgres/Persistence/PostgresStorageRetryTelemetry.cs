using System.Diagnostics.Metrics;

using Edict.Telemetry;

namespace Edict.Postgres.Persistence;

// Counter holder for grain-storage transient retries, on the single shared
// EdictDiagnostics.Meter. A rising rate means the Postgres substrate is
// shedding transient faults under load; an exhausted slice means retries are
// no longer clearing them and EdictPostgresStorageException is surfacing.
// Both tag allowlists are closed at compile time so the dimension stays bounded.
static class PostgresStorageRetryTelemetry
{
    public const string Read = "Read";
    public const string Write = "Write";
    public const string Clear = "Clear";

    public const string MeterName = "edict.postgres.storage.retry.count";
    public const string OperationTag = "edict.postgres.storage.operation";
    public const string OutcomeTag = "edict.postgres.storage.outcome";
    public const string Recovered = "recovered";
    public const string Exhausted = "exhausted";

    static readonly Counter<long> RetryCount = EdictDiagnostics.Meter.CreateCounter<long>(MeterName);

    public static void RecordRecovered(string operation) => Record(operation, Recovered);

    public static void RecordExhausted(string operation) => Record(operation, Exhausted);

    static void Record(string operation, string outcome) =>
        RetryCount.Add(1,
            new KeyValuePair<string, object?>(OperationTag, operation),
            new KeyValuePair<string, object?>(OutcomeTag, outcome));
}
