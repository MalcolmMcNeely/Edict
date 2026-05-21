using Edict.Contracts.Commands;

namespace Edict.Core.Sagas;

sealed class SagaDispatchBuffer
{
    EdictCommand? _pending;

    public void Set(EdictCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_pending is not null)
        {
            throw new InvalidOperationException(
                "A saga issues exactly one Command per Event; Dispatch was called more than once " +
                "within a single event handler. Command fan-out from a saga is a coordination smell.");
        }

        _pending = command;
    }

    public void Reset() => _pending = null;

    public EdictCommand? Take()
    {
        var command = _pending;
        _pending = null;
        return command;
    }
}
