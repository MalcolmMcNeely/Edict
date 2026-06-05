using Edict.Contracts.Projections;

namespace Edict.Core.Correlation;

/// <summary>
/// The in-grain read-your-writes wait mechanism. Parks read waiters keyed by
/// correlation id, signals them when their correlation is marked processed,
/// enforces a bounded (or explicit-indefinite) timeout against an injected
/// <see cref="TimeProvider"/>, and resolves the <see cref="EdictReadStatus"/>
/// tri-state. Membership consults an in-memory mirror updated <em>after</em> the
/// row write has drained, so a <see cref="EdictReadStatus.CursorReached"/> answer
/// implies the row is readable. The mirror is seeded from the persisted ring at
/// grain activation, where drain-on-activation has already made the store
/// consistent. Lives on the single-threaded grain activation, so its state needs
/// no locking; an in-flight wait pins the activation, so an indefinite waiter's
/// task survives.
/// </summary>
sealed class CursorWaitCoordinator
{
    readonly CorrelationRingMirror _mirror = new();
    readonly Dictionary<Guid, List<TaskCompletionSource>> _waiters = [];

    /// <summary>
    /// Resolves the effective wait from the caller's request: an explicit value
    /// (including <see cref="Timeout.InfiniteTimeSpan"/> for an indefinite wait) is
    /// honoured; an omitted value falls back to the bounded
    /// <paramref name="boundedDefault"/>, never to an infinite wait, so a forgotten
    /// timeout cannot hang a request.
    /// </summary>
    public static TimeSpan ResolveTimeout(TimeSpan? requested, TimeSpan boundedDefault) => requested ?? boundedDefault;

    /// <summary>Seeds the membership mirror from the persisted ring at grain activation.</summary>
    public void Activate(Guid[] persistedRing, int head, int count) => _mirror.Activate(persistedRing, head, count);

    /// <summary>
    /// Marks <paramref name="correlationId"/> visible (called after the row write
    /// has drained): advances the membership mirror and signals every waiter
    /// parked on that correlation.
    /// </summary>
    public void MarkProcessed(Guid correlationId)
    {
        _mirror.Commit(correlationId);

        if (_waiters.Remove(correlationId, out var parked))
        {
            foreach (var waiter in parked)
            {
                waiter.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Waits until <paramref name="correlationId"/> is visible or the wait elapses.
    /// A caught-up correlation answers <see cref="EdictReadStatus.CursorReached"/>
    /// without waiting. Otherwise the call parks until signalled
    /// (<see cref="EdictReadStatus.CursorReached"/>) or
    /// <paramref name="timeout"/> elapses against <paramref name="timeProvider"/>
    /// (<see cref="EdictReadStatus.CursorTimedOut"/>). An explicit
    /// <see cref="Timeout.InfiniteTimeSpan"/> never times out. Caller cancellation
    /// surfaces <see cref="OperationCanceledException"/> — the one legitimate throw.
    /// </summary>
    public async Task<EdictReadStatus> WaitAsync(Guid correlationId, TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (_mirror.Contains(correlationId))
        {
            return EdictReadStatus.CursorReached;
        }

        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Park(correlationId, waiter);

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, timeProvider, timeoutCancellation.Token);

        try
        {
            var winner = await Task.WhenAny(waiter.Task, delay).ConfigureAwait(false);
            if (winner == waiter.Task)
            {
                return EdictReadStatus.CursorReached;
            }

            // The delay won: surface caller cancellation as a throw, otherwise a
            // genuine timeout. Task.Delay completes (not faults) on its own elapse
            // and faults with cancellation only when the caller's token fired.
            cancellationToken.ThrowIfCancellationRequested();
            return EdictReadStatus.CursorTimedOut;
        }
        finally
        {
            timeoutCancellation.Cancel();
            Unpark(correlationId, waiter);
        }
    }

    void Park(Guid correlationId, TaskCompletionSource waiter)
    {
        if (!_waiters.TryGetValue(correlationId, out var parked))
        {
            parked = [];
            _waiters[correlationId] = parked;
        }
        parked.Add(waiter);
    }

    void Unpark(Guid correlationId, TaskCompletionSource waiter)
    {
        if (_waiters.TryGetValue(correlationId, out var parked) && parked.Remove(waiter) && parked.Count == 0)
        {
            _waiters.Remove(correlationId);
        }
    }
}
