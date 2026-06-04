using Edict.Contracts.Events;

using Orleans;

namespace Edict.Tests.Conformance.Outbox;

static class CounterEventWaiters
{
    public static async Task<IReadOnlyList<EdictEvent>> WaitForEventsAsync(
        IGrainFactory grainFactory, Guid counterId, int expectedCount = 1, int timeoutSeconds = 20)
    {
        var captureGrain = grainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
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
