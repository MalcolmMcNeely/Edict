namespace Edict.Azure.Tests.EventHandler;

// Shared probe-polling helpers for the EventHandler scenario suite. Per-test
// helpers duplicated the same poll-until-deadline shape across the lifted
// files; this is the same body each used.
static class EventHandlerWaiters
{
    public static async Task<IReadOnlyList<Guid>> WaitForHandledAsync(
        IAzureEmailHandlerProbe handler, int expectedCount = 1, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await handler.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await handler.GetHandledEventIdsAsync();
    }
}
