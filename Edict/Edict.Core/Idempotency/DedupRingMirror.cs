namespace Edict.Core.Idempotency;

/// <summary>
/// In-memory HashSet mirror of the persisted dedup ring. Turns
/// <see cref="Contains"/> into an O(1) lookup so a consumer can size
/// <see cref="EdictIdempotencyBase{TPayload}.WindowSize"/> up without paying a
/// per-event linear scan tax. The HashSet is rebuilt on grain activation from
/// the canonical persisted ring (<see cref="IdempotencyState.HandledEventIds"/>)
/// and kept in sync as the ring rotates — the array stays the durable state, the
/// set never touches grain state.
/// </summary>
sealed class DedupRingMirror
{
    readonly HashSet<Guid> _set = [];
    Guid[] _ring = [];
    int _head;
    int _count;

    /// <summary>
    /// Rebuilds the in-memory set from the persisted ring. Copies the populated
    /// slot range so the mirror's notion of head/count stays in lockstep with
    /// the canonical state, then populates the set from the
    /// <paramref name="count"/> populated slots only.
    /// </summary>
    public void Activate(Guid[] persistedRing, int head, int count)
    {
        _ring = new Guid[persistedRing.Length];
        Array.Copy(persistedRing, _ring, persistedRing.Length);
        _head = head;
        _count = count;
        _set.Clear();

        if (count == _ring.Length)
        {
            foreach (var id in _ring)
            {
                _set.Add(id);
            }
        }
        else
        {
            // Populated slots are the first `count` entries (Head wraps to 0 on
            // first commit so before the ring fills, the populated range is
            // [0, count) and Head == count).
            for (var i = 0; i < count; i++)
            {
                _set.Add(_ring[i]);
            }
        }
    }

    public bool Contains(Guid eventId) => _set.Contains(eventId);

    /// <summary>
    /// Records a commit at the ring's current head position. When the ring is
    /// full, the slot at <c>head</c> is overwritten, so the id previously held
    /// there is evicted from the in-memory set before the new id is added.
    /// Mirrors the caller's persisted-ring update; call once after the
    /// canonical write completes.
    /// </summary>
    public void Commit(Guid eventId)
    {
        if (_count == _ring.Length)
        {
            var displaced = _ring[_head];
            _set.Remove(displaced);
        }

        _ring[_head] = eventId;
        _head = (_head + 1) % _ring.Length;

        if (_count < _ring.Length)
        {
            _count++;
        }

        _set.Add(eventId);
    }
}
