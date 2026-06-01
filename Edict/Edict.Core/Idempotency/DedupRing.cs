namespace Edict.Core.Idempotency;

// Pure, revertable transitions over the persisted dedup ring
// (IdempotencyState). Apply writes the new id into the head slot so it rides
// the grain-state write that makes the commit durable; RollBack restores the
// exact prior bookkeeping when that write faults, so a redelivery to a still-live
// activation is re-dispatched rather than suppressed against an id the store
// never persisted. The in-memory DedupRingMirror is advanced separately, only
// after the canonical write completes.
static class DedupRing
{
    public readonly record struct Revert(int Head, int Count, Guid Displaced);

    public static Revert Apply(IdempotencyState ring, int windowSize, Guid eventId)
    {
        var head = ring.Head;
        var revert = new Revert(head, ring.Count, ring.HandledEventIds[head]);

        ring.HandledEventIds[head] = eventId;
        ring.Head = (head + 1) % windowSize;
        if (ring.Count < windowSize)
        {
            ring.Count++;
        }

        return revert;
    }

    public static void RollBack(IdempotencyState ring, Revert revert)
    {
        ring.HandledEventIds[revert.Head] = revert.Displaced;
        ring.Head = revert.Head;
        ring.Count = revert.Count;
    }
}
