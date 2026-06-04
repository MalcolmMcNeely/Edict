namespace Edict.Postgres.Tests.Resilience;

static class PostgresResilienceWaiters
{
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 90)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
}
