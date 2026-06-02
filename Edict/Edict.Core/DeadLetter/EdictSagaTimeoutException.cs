namespace Edict.Core.DeadLetter;

/// <summary>
/// Cause attached to the dead-letter row when a saga's absolute lifetime cap
/// fires and the consumer did not override the timeout compensation hook. A
/// forgotten timeout surfaces loudly on the dead-letter projection rather than
/// vanishing. The framework keys dead-letter rows, span events, and catch sites
/// on a stable <c>Edict*</c> type; this one maps to its own <c>SagaTimeout</c>
/// failure-reason bucket.
/// </summary>
public sealed class EdictSagaTimeoutException : Exception
{
    public EdictSagaTimeoutException(string message)
        : base(message)
    {
    }
}
