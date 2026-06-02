using Edict.Contracts.Commands;
using Edict.Core.DeadLetter;

namespace Edict.Core.Sagas;

sealed class SagaDispatchBuffer
{
    EdictCommand? _pending;

    public void Set(EdictCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_pending is not null)
        {
            throw new EdictSagaCoordinationException(
                "A saga issues exactly one Command per Event; Dispatch was called more than once " +
                "within a single event handler. Command fan-out from a saga is a coordination smell.");
        }

        _pending = command;
    }

    public EdictCommand? Take()
    {
        var command = _pending;
        _pending = null;
        return command;
    }

    /// <summary>Set when the handler called <c>Complete()</c>; read back by the dispatch after the handler returns.</summary>
    public bool CompleteRequested { get; private set; }

    public void RequestComplete() => CompleteRequested = true;

    /// <summary>Set when the default timeout hook ran; the fire path stages a dead-letter rather than a compensating Command.</summary>
    public bool DeadLetterRequested { get; private set; }

    public void RequestDeadLetter() => DeadLetterRequested = true;
}
