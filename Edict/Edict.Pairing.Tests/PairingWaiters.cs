namespace Edict.Pairing.Tests;

static class PairingWaiters
{
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 60)
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
