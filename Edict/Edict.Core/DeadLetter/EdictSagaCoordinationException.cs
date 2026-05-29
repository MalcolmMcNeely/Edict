namespace Edict.Core.DeadLetter;

/// <summary>
/// Runtime fault raised when a saga's <c>Handle</c> method violates the
/// one-command-per-event coordination rule (e.g. <c>Dispatch</c> called
/// more than once within a single event handler). The fault is the
/// consumer's saga code, not framework wiring, so it maps to the
/// <c>ConsumerBug</c> failure-reason bucket.
/// </summary>
public sealed class EdictSagaCoordinationException : Exception
{
    public EdictSagaCoordinationException(string message)
        : base(message)
    {
    }
}
