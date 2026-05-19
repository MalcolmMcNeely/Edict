using Edict.Contracts.Commands;
using Edict.Contracts.Sending;

namespace Edict.Testing;

/// <summary>
/// Decorates the real <see cref="IEdictSender"/> so every Command — whether
/// issued by the test client or staged by a saga's SendCommand effect draining
/// in-silo — lands on the timeline, then delegates to the genuine sender. The
/// consumer's code path is unchanged; this only observes.
/// </summary>
sealed class RecordingEdictSender(IEdictSender inner, EdictTimelineRecorder recorder) : IEdictSender
{
    public Task<EdictCommandResult> Send(EdictCommand command)
    {
        recorder.RecordCommand(command);
        return inner.Send(command);
    }
}
