namespace Edict.Core.Correlation;

/// <summary>
/// Pure ring transitions over <see cref="CorrelationProgress"/>, modelled directly
/// on the dedup ring: <see cref="Apply"/> writes the correlation at the head and
/// advances, returning the bookkeeping needed to undo it exactly if the commit
/// write faults; <see cref="RollBack"/> restores that bookkeeping. No grain state
/// or clock is touched, so the transitions test as plain values.
/// </summary>
static class CorrelationRing
{
    public readonly record struct Revert(int Head, int Count, Guid Displaced);

    public static Revert Apply(CorrelationProgress ring, int windowSize, Guid correlationId)
    {
        var head = ring.Head;
        var revert = new Revert(head, ring.Count, ring.CorrelationIds[head]);

        ring.CorrelationIds[head] = correlationId;
        ring.Head = (head + 1) % windowSize;
        if (ring.Count < windowSize)
        {
            ring.Count++;
        }
        return revert;
    }

    public static void RollBack(CorrelationProgress ring, Revert revert)
    {
        ring.CorrelationIds[revert.Head] = revert.Displaced;
        ring.Head = revert.Head;
        ring.Count = revert.Count;
    }
}
