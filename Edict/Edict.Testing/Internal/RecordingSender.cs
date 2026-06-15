using System.Collections.Concurrent;

using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Contracts.Sending;
using Edict.Core.Commands;

namespace Edict.Testing.Internal;

/// <summary>
/// Decorates the real <see cref="IEdictSender"/> so every Command — whether
/// issued by the test client or staged by a saga's SendCommand effect draining
/// in-silo — lands on the timeline, then delegates to the genuine sender. Also
/// records the routed <c>(grainClassName, key)</c> so the Test Framework's
/// <c>FireDueSchedulesAsync</c> seam can discover every grain that might hold a
/// schedule without a production-side registry.
/// </summary>
sealed class RecordingSender(
    IEdictSender inner,
    TimelineRecorder recorder,
    CommandRouteResolver resolver,
    ConcurrentDictionary<(string GrainClassName, Guid Key), byte> routedGrains) : IEdictSender
{
    public Task<EdictCommandResult> SendAsync(EdictCommand command)
    {
        recorder.RecordCommand(command);

        try
        {
            var route = resolver.GetRoute(command);
            routedGrains.TryAdd((route.GrainClassName, route.RouteKeySelector(command)), 0);
        }
        catch
        {
            // An unroutable command fails identically in the inner sender below;
            // it simply contributes no grain to the schedule-fire roster.
        }

        return inner.SendAsync(command);
    }

    public Task<EdictCommandResult> SendAsync(EdictCommand command, EdictPrincipal principal) =>
        SendAsync(command with { Principal = principal });
}
