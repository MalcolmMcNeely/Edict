using Edict.Contracts.Schedules;

namespace Edict.Core.Schedules;

/// <summary>
/// Thrown when a fired <see cref="EdictScheduleMessage"/> has no generator-emitted
/// dispatch arm — i.e. the grain that persisted the schedule declares no
/// <c>HandleAsync(TMessage) : Task&lt;EdictScheduleResult&gt;</c> for its type.
/// This is a wiring fault (a removed fire handler, or a message that escaped the
/// grain that owns it), not a business outcome, so it throws. Reached only as the
/// switch's safety net; a schedule can only be armed for a message the same grain
/// can dispatch.
/// </summary>
public sealed class EdictUnroutableScheduleMessageException(Type messageType)
    : InvalidOperationException(
        $"No schedule fire handler dispatches message '{messageType.FullName}'. " +
        $"Declare a HandleAsync({messageType.Name}) : Task<EdictScheduleResult> overload " +
        "on the Command Handler that arms the schedule.")
{
    /// <summary>The schedule-message type that could not be routed.</summary>
    public Type MessageType { get; } = messageType;
}
