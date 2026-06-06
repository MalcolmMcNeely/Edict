namespace Edict.Postgres.Persistence;

// Postgres-specific transient-retry loop. Deep and isolated: it knows nothing
// about Npgsql, grain storage, metrics, or logging — the caller supplies the
// operation, the policy, the transient predicate, and a side-effect hook, and
// the loop returns the operation's result or rethrows the last fault after the
// budget is exhausted. A non-transient fault is never caught, so it propagates
// on the first attempt with no delay.
static class PostgresTransientRetry
{
    public enum Phase
    {
        Retrying,
        Recovered,
        Exhausted,
    }

    public delegate void Hook(Phase phase, int attemptNumber, Exception? exception);

    public readonly record struct Policy(int TotalAttempts, TimeSpan BaseDelay)
    {
        public static Policy Default => new(TotalAttempts: 3, BaseDelay: TimeSpan.FromMilliseconds(50));
    }

    public static async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> operation,
        Policy policy,
        Func<Exception, bool> isTransient,
        Hook hook)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await operation();
                if (attempt > 1)
                {
                    hook(Phase.Recovered, attempt, null);
                }
                return result;
            }
            catch (Exception exception) when (isTransient(exception))
            {
                if (attempt >= policy.TotalAttempts)
                {
                    hook(Phase.Exhausted, attempt, exception);
                    throw;
                }
                hook(Phase.Retrying, attempt, exception);
                // Exponential, no jitter: BaseDelay, then 2×, then 4×, ...
                await Task.Delay(policy.BaseDelay * (1 << (attempt - 1)));
            }
        }
    }

    public static Task ExecuteAsync(
        Func<Task> operation,
        Policy policy,
        Func<Exception, bool> isTransient,
        Hook hook) =>
        ExecuteAsync(
            async () =>
            {
                await operation();
                return true;
            },
            policy,
            isTransient,
            hook);
}
