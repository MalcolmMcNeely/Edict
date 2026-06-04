namespace Edict.Tests.Conformance.Outbox;

static class OutboxProbeWaiters
{
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
