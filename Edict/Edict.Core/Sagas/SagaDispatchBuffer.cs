using Edict.Contracts.Commands;

namespace Edict.Core.Sagas;

/// <summary>
/// The single outbound-command slot behind <c>EdictSaga&lt;TProgress&gt;.Dispatch</c>
/// (ADR 0020). Pure, no Orleans coupling, so the one-command-per-event hard
/// limit is unit-tested in isolation without a cluster. <see cref="Set"/>
/// throws on a second call within one event handler; <see cref="Reset"/> scopes
/// the limit to a single Event; <see cref="Take"/> hands the buffered command
/// to the Outbox-staging hook and clears the slot. Bare-named — no consumer
/// types it.
/// </summary>
sealed class SagaDispatchBuffer
{
    EdictCommand? _pending;

    /// <summary>
    /// Buffers the single Command this Event implies. A second call within one
    /// event handler is a hard error — a saga that fans out commands is a
    /// coordination smell, and the single-command API shape makes that
    /// constraint structural rather than advisory (ADR 0020).
    /// </summary>
    public void Set(EdictCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_pending is not null)
        {
            throw new InvalidOperationException(
                "A saga issues exactly one Command per Event; Dispatch was called more than once " +
                "within a single event handler. Command fan-out from a saga is a coordination smell " +
                "(ADR 0020).");
        }

        _pending = command;
    }

    /// <summary>Clears the slot before each handler so the limit is scoped to one Event.</summary>
    public void Reset() => _pending = null;

    /// <summary>Returns and clears the buffered command, or <c>null</c> when none was dispatched.</summary>
    public EdictCommand? Take()
    {
        var command = _pending;
        _pending = null;
        return command;
    }
}
