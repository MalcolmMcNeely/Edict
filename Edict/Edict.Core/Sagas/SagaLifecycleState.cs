using System.ComponentModel;

namespace Edict.Core.Sagas;

/// <summary>
/// A saga's terminal-state lifecycle. <see cref="Live"/> accepts new Events;
/// <see cref="Completed"/> (consumer called <c>Complete()</c>) and
/// <see cref="TimedOut"/> (the absolute cap fired) are both terminal — a
/// genuinely-new Event at either dead-letters.
/// <para>
/// Public because it rides as a property on the persisted
/// <see cref="SagaLifecycle"/> slot of the public
/// <c>GrainEnvelope&lt;TPayload&gt;</c>; hidden from consumer IntelliSense
/// because the consumer never types it — the framework owns lifecycle commits.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum SagaLifecycleState
{
    Live = 0,
    Completed = 1,
    TimedOut = 2,
}
