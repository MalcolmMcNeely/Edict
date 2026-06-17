namespace Edict.Tests.Conformance;

// Generic deadline-bounded poll for scenarios whose subject settles asynchronously
// off the turn that triggered it — a row landing in a real store, a drain converging
// over repeated reminder ticks, a projection catching up. The pacing delay lives
// here, off the *Scenarios surface, so a scenario states the settled condition while
// this drives the retry loop. The condition runs before the first pause, so a body
// that itself nudges the system forward (forcing a drain, recording a gauge) fires
// each iteration; the pause between iterations lets the resulting async work — and
// any outbox backoff — elapse.
static class ConformanceWaiters
{
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 30)
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
