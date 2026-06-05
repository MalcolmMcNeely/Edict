using System.ComponentModel;

namespace Edict.Core.Schedules;

/// <summary>
/// One durable recurring schedule co-located in the grain-state envelope. Carries
/// the serialized <c>EdictScheduleMessage</c> (Orleans bytes that self-describe
/// the concrete type, so a fire deserializes and re-dispatches it by type), the
/// declared <see cref="Period"/>, and the next <see cref="DueAt"/> instant.
/// Immutable: the slice's pure transitions produce new entries, never mutate in
/// place. Persisted grain state, so a frozen string-literal <c>[Alias]</c>
/// survives a class rename; <c>ORLEANS0010</c> is never suppressed.
/// <para>
/// Public because it rides as a list element on <c>ScheduleSlice.Active</c>,
/// which rides on the public <c>GrainEnvelope&lt;TPayload&gt;</c>. Hidden from
/// consumer IntelliSense because the consumer never types this — the framework
/// arms and fires schedules.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
[Alias("ScheduleEntry")]
public sealed record ScheduleEntry
{
    [Id(0)]
    public Guid ScheduleId { get; init; }

    /// <summary>The serialized <c>EdictScheduleMessage</c>, opaque to the slice.</summary>
    [Id(1)]
    public byte[] MessagePayload { get; init; } = [];

    /// <summary>The declared <c>every:</c> cadence, the single source of the interval.</summary>
    [Id(2)]
    public TimeSpan Period { get; init; }

    /// <summary>The next instant this schedule is due to fire.</summary>
    [Id(3)]
    public DateTimeOffset DueAt { get; init; }

    /// <summary>
    /// The absolute instant this schedule hits its timeout cap, armed once at
    /// registration and never advanced by a fire — a dead-man's switch a healthy
    /// recurring schedule cannot push forever. <c>null</c> when the schedule is
    /// uncapped (an explicit <c>Unbounded</c> opt-out, or an inherited silo
    /// default that was itself null).
    /// </summary>
    [Id(4)]
    public DateTimeOffset? DeadlineAt { get; init; }
}
