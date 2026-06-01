namespace Edict.Core.Idempotency;

// Holds per-dispatch state that the consumer writes through `this` during a
// handler — a saga's buffered command, a projection's working row — isolated
// across interleaved dispatches on one grain. The outbox drain runs ready
// entries in parallel, so several deferred InvokeHandler dispatches can be in
// flight on the grain scheduler at once and interleave at every await; a shared
// field would cross-wire their state. Each dispatch gets its own AsyncLocal
// value via Begin, which flows down to the handler (a callee), while the
// dispatch reads its state back from the returned reference — not the
// AsyncLocal, which would not flow a callee's mutation back to its caller.
sealed class InvocationScope<TState>
    where TState : class, new()
{
    readonly AsyncLocal<TState?> _current = new();

    public TState Current =>
        _current.Value ?? throw new InvalidOperationException(
            "No dispatch is in scope; this state is only available inside a handler invocation.");

    public TState Begin()
    {
        var state = new TState();
        _current.Value = state;
        return state;
    }
}
