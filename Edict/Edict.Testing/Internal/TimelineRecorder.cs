using System.Collections.Concurrent;
using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Edict.Testing.Internal;

sealed class TimelineRecorder
{
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

    public void RecordEvent(EdictEvent edictEvent) =>
        _entries.Enqueue(new TimelineEntry("Event", edictEvent.GetType().Name, Payload(edictEvent)));

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
