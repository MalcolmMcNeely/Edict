using System.Diagnostics.Metrics;

using Edict.Telemetry;

namespace Edict.Core.Outbox;

// Reminder-registration cold-start retry counter on the single shared EdictDiagnostics.Meter.
// Held in a non-generic static so the initializer runs once per process. Both tag allowlists
// are closed at compile time so the dimension stays bounded; recorded once on recovery and
// once on exhaustion, never per attempt.
static class ReminderRegistrationMetrics
{
    static readonly Counter<long> RetryCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Reminders.Meters.RegistrationRetryCount);

    public static void RecordRecovered(string operation) =>
        Record(operation, SemanticConventions.Reminders.Tags.OutcomeValues.Recovered);

    public static void RecordExhausted(string operation) =>
        Record(operation, SemanticConventions.Reminders.Tags.OutcomeValues.Exhausted);

    static void Record(string operation, string outcome) =>
        RetryCount.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Reminders.Tags.Operation, operation),
            new KeyValuePair<string, object?>(SemanticConventions.Reminders.Tags.Outcome, outcome));
}
