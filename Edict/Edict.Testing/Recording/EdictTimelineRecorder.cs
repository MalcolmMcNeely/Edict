using System.Collections.Concurrent;
using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Edict.Testing.Recording;

/// <summary>
/// Process-side sink the capture decorators write to. One instance per
/// <see cref="EdictTestApp"/> (shared by the silo and client containers via the
/// harness registry) so a saga's in-silo dispatched Command and a test's
/// client-side Command land on the same ordered timeline.
/// </summary>
sealed class EdictTimelineRecorder
{
    // The framework-owned envelope fields. Excluded from the rendered payload so
    // the Verify snapshot is deterministic — only the consumer's domain data
    // remains (ids/timestamps/trace ctx are stamped by the runtime).
    static readonly HashSet<string> EnvelopeFields =
    [
        nameof(EdictCommand.CommandId),
        nameof(EdictEvent.EventId),
        nameof(EdictEvent.OccurredAt),
        nameof(EdictEvent.TraceId),
        nameof(EdictEvent.SpanId),
        nameof(EdictEvent.TraceState),
    ];

    readonly ConcurrentQueue<TimelineEntry> _entries = new();

    public int Count => _entries.Count;

    public void RecordCommand(EdictCommand command) =>
        _entries.Enqueue(new TimelineEntry("Command", command.GetType().Name, Payload(command)));

    public void RecordEvent(EdictEvent evt) =>
        _entries.Enqueue(new TimelineEntry("Event", evt.GetType().Name, Payload(evt)));

    /// <summary>
    /// Records an <see cref="Edict.Core.EventHandler.EdictEventHandler"/>'s
    /// deferred invocation: a single entry per drained
    /// <c>InvokeHandler</c> Outbox effect, carrying the source event type, the
    /// source event id and the outcome — <c>Ran</c> when the consumer's
    /// <c>Handle</c> returned, or <c>DeadLettered</c> when the engine exhausted
    /// <c>MaxAttempts</c> and promoted the entry. Surfaces "event arrived →
    /// ran (or dead-lettered)" explicitly on the timeline alongside
    /// <c>Command</c> and <c>Event</c>.
    /// </summary>
    public void RecordInvocation(string sourceEventType, Guid sourceEventId, string outcome) =>
        _entries.Enqueue(new TimelineEntry(
            "Invocation",
            sourceEventType,
            new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["EventId"] = sourceEventId,
                ["Outcome"] = outcome,
            }));

    public Timeline Snapshot() => new([.. _entries]);

    static IReadOnlyDictionary<string, object?> Payload(object message)
    {
        var data = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in message.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || EnvelopeFields.Contains(property.Name))
            {
                continue;
            }
            data[property.Name] = property.GetValue(message);
        }
        return data;
    }
}
