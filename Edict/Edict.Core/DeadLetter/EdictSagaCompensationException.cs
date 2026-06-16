namespace Edict.Core.DeadLetter;

/// <summary>
/// Cause attached to the dead-letter row when a saga's <c>OnSagaTimeoutAsync</c>
/// override throws while the absolute lifetime cap is firing. The cap handler
/// contains the throw, rolls back any half-applied compensation, terminalises the
/// saga, and dead-letters with this cause so a faulting compensation reads apart
/// from the by-design <c>SagaTimeout</c> (no override, requested dead-letter). The
/// fault is the consumer's compensation code, so it maps to the <c>ConsumerBug</c>
/// failure-reason bucket; the originally-thrown exception rides as
/// <see cref="System.Exception.InnerException"/>.
/// </summary>
public sealed class EdictSagaCompensationException : Exception
{
    public EdictSagaCompensationException(string message)
        : base(message)
    {
    }

    public EdictSagaCompensationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
