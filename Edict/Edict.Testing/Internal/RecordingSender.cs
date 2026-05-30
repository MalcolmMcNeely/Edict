using Edict.Contracts.Commands;
using Edict.Contracts.Sending;

namespace Edict.Testing.Internal;

/// <summary>
/// Decorates the real <see cref="IEdictSender"/> so every Command — whether
/// issued by the test client or staged by a saga's SendCommand effect draining
/// in-silo — lands on the timeline, then delegates to the genuine sender.
/// </summary>
sealed class RecordingSender(IEdictSender inner, TimelineRecorder recorder) : IEdictSender
{
    public Task<EdictCommandResult> SendAsync(EdictCommand command)
    {
        recorder.RecordCommand(command);
        return inner.SendAsync(command);
    }
}
