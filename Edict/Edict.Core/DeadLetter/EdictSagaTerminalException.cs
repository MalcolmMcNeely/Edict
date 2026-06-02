namespace Edict.Core.DeadLetter;

/// <summary>
/// Cause attached to the dead-letter row when a genuinely-new Event arrives at a
/// terminal saga (one that called <c>Complete()</c> or whose cap fired). "The
/// Event I gave up on finally arrived" demands attention rather than being
/// silently dropped against a compensated reality. A plain at-least-once
/// redelivery of an already-handled Event is suppressed by the dedup ring before
/// this fires. It maps to its own <c>SagaTerminal</c> failure-reason bucket so
/// the two saga-lifecycle failures read apart on the dead-letter projection.
/// </summary>
public sealed class EdictSagaTerminalException : Exception
{
    public EdictSagaTerminalException(string message)
        : base(message)
    {
    }
}
