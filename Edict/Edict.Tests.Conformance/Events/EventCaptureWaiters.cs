using Edict.Contracts.Events;

using Orleans;

namespace Edict.Tests.Conformance.Events;

static class EventCaptureWaiters
{
    public static async Task<IReadOnlyList<EdictEvent>> WaitForEventsAsync(
        IGrainFactory grainFactory, Guid aggregateId, int expectedCount = 1, int timeoutSeconds = 30)
    {
        var captureGrain = grainFactory.GetGrain<IOrderEventCaptureGrain>(aggregateId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await captureGrain.GetCapturedEventsAsync();
            if (events.Count >= expectedCount)
            {
                return events;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await captureGrain.GetCapturedEventsAsync();
    }
}
