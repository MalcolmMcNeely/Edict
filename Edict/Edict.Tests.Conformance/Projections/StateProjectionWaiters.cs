namespace Edict.Tests.Conformance.Projections;

static class StateProjectionWaiters
{
    // Settles once the projected total reaches the expected final value. A trailing
    // distinct event is the sentinel that proves every earlier delivery — including
    // a same-EventId duplicate sent ahead of it on the serially-delivered stream —
    // has been applied or suppressed, so the scenario's equality assertion that
    // follows catches an over-count rather than racing an in-flight duplicate.
    public static async Task WaitForTotalAsync(
        IRunningTotalProjectionProbe probe,
        int expectedTotal,
        int timeoutSeconds = 45)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await probe.GetTotalAsync() >= expectedTotal)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
