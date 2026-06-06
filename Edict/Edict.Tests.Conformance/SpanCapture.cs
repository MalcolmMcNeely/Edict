using System.Diagnostics;

using Edict.Telemetry;

namespace Edict.Tests.Conformance;

/// <summary>
/// Captures Edict spans for assertion across the conformance batteries and the
/// provider test projects that bind them. <c>ActivityStopped</c> fires when a
/// span's using-scope unwinds; for a deferred handle or invocation span that is a
/// separate, later signal than any handler or event-count probe a scenario might
/// await. Waiting on a probe and then querying the list races the span emission,
/// so scenarios must wait on the span they assert on via <see cref="WaitForSpanAsync"/>.
/// Add and read are synchronized because <c>ActivityStopped</c> fires on arbitrary
/// grain threads while the scenario enumerates.
/// </summary>
public sealed class SpanCapture : IDisposable
{
    readonly List<Activity> _stopped = [];
    readonly Lock _gate = new();
    readonly ActivityListener _listener;

    public SpanCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (_gate) { _stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public async Task<Activity> WaitForSpanAsync(Func<Activity, bool> predicate, string description, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            lock (_gate)
            {
                var match = _stopped.FirstOrDefault(predicate);
                if (match is not null)
                {
                    return match;
                }
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"No span matched {description} within {timeoutSeconds}s. Stopped spans: {SeenNames()}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    public IReadOnlyList<Activity> Snapshot()
    {
        lock (_gate)
        {
            return _stopped.ToList();
        }
    }

    string SeenNames()
    {
        lock (_gate)
        {
            return _stopped.Count == 0 ? "(none)" : string.Join(", ", _stopped.Select(activity => activity.OperationName));
        }
    }

    public void Dispose() => _listener.Dispose();
}
