namespace Edict.Core.DeadLetter;

/// <summary>
/// Runtime fault for framework-internal invariants that "should never
/// happen" — defensive guards on values Edict itself produced (Orleans
/// QueueId prefixes, reflection-shaped serializer lookups, generator
/// outputs). A throw here is a framework bug, not a consumer fault, so
/// it maps to the <c>InternalBug</c> failure-reason bucket and signals
/// "open a framework issue" to the operator.
/// </summary>
public sealed class EdictInternalInvariantException : Exception
{
    public EdictInternalInvariantException(string message)
        : base(message)
    {
    }
}
