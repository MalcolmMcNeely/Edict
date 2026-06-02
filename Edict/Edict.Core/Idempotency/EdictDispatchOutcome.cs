using System.ComponentModel;

using Edict.Core.Outbox;

namespace Edict.Core.Idempotency;

/// <summary>
/// The result of one consumer dispatch, threaded back through the generated
/// <c>DispatchAsync</c> override. <see cref="Handled"/> distinguishes a matched
/// event type (ring slot consumed) from an unhandled one (pure no-op);
/// <see cref="StagedEffect"/> carries the single downstream effect a saga or
/// table-projection builder staged during the handler (a
/// <c>SendCommand</c> / <c>UpsertRow</c> entry), or <c>null</c> for an event
/// handler and the in-memory projection builder.
/// <para>
/// Staging is invocation-scoped: the effect rides this return value rather than
/// a grain field, so a parallel crash-recovery drain that runs several deferred
/// dispatches at once cannot lose or cross-wire one dispatch's effect into
/// another's.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct EdictDispatchOutcome
{
    internal EdictDispatchOutcome(bool handled, OutboxEntry? stagedEffect, bool completeRequested = false)
    {
        Handled = handled;
        StagedEffect = stagedEffect;
        CompleteRequested = completeRequested;
    }

    /// <summary><c>true</c> if the event type matched a handler arm.</summary>
    public bool Handled { get; }

    internal OutboxEntry? StagedEffect { get; }

    /// <summary>
    /// <c>true</c> if the saga handler called <c>Complete()</c>. Rides the return
    /// value rather than a grain field so a parallel deferred dispatch cannot
    /// cross-wire one handler's completion onto another's commit.
    /// </summary>
    internal bool CompleteRequested { get; }

    /// <summary>A matched dispatch that staged no downstream effect.</summary>
    public static EdictDispatchOutcome HandledWithNoEffect { get; } = new(handled: true, stagedEffect: null);

    /// <summary>The event type matched no handler arm; no ring slot is consumed.</summary>
    public static EdictDispatchOutcome NotHandled { get; } = new(handled: false, stagedEffect: null);

    internal static EdictDispatchOutcome HandledWith(OutboxEntry? stagedEffect) => new(handled: true, stagedEffect);

    internal static EdictDispatchOutcome HandledWith(OutboxEntry? stagedEffect, bool completeRequested) =>
        new(handled: true, stagedEffect, completeRequested);
}
