using System.ComponentModel;

namespace Edict.Core.Sagas;

/// <summary>
/// The saga-lifecycle slot of the grain-state envelope: the terminal-state
/// machine plus the absolute <see cref="DeadlineAt"/>, persisted so a
/// reactivated grain re-derives remaining time and a cap reminder fire is
/// verifiable. The slot's presence (a non-null
/// <c>GrainEnvelope&lt;TPayload&gt;.Saga</c>) marks that the saga has started —
/// its cap was armed on the first handled Event and is never reset by later
/// activity. A null <see cref="DeadlineAt"/> on a present slot means the saga
/// is unbounded (started, no cap). Only sagas populate this slot; every other
/// idempotent consumer leaves it null.
/// <para>
/// Public because it rides as a slot on the public
/// <c>GrainEnvelope&lt;TPayload&gt;</c>; hidden from consumer IntelliSense
/// because the consumer never types it. Persisted state, so a frozen
/// string-literal <c>[Alias]</c> survives a class rename.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
[Alias("SagaLifecycle")]
public sealed record SagaLifecycle
{
    [Id(0)]
    public SagaLifecycleState State { get; init; } = SagaLifecycleState.Live;

    [Id(1)]
    public DateTimeOffset? DeadlineAt { get; init; }

    /// <summary>
    /// The W3C <c>traceparent</c> of the context that armed this saga on its first
    /// handled event, persisted so a fired cap — its own grain turn, far removed in
    /// time and possibly across a deactivation — links back to that context rather
    /// than orphaning. <c>null</c> when the arming event carried no trace context.
    /// </summary>
    [Id(2)]
    public string? ArmTraceParent { get; init; }

    /// <summary>The <c>tracestate</c> companion to <see cref="ArmTraceParent"/>, riding onto the cap fire's link.</summary>
    [Id(3)]
    public string? ArmTraceState { get; init; }
}
