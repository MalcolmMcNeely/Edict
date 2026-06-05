namespace Edict.Core.Correlation;

/// <summary>
/// In-memory HashSet mirror of the persisted correlation ring, modelled on the
/// dedup mirror. Answers "has this correlation been processed" as an O(1) lookup so
/// a read-your-writes read can decide immediately whether to answer or park. The
/// set is rebuilt on grain activation from the canonical persisted ring
/// (<see cref="CorrelationProgress.CorrelationIds"/>) and kept in sync as the ring
/// rotates — the array stays the durable state, the set never touches grain state.
/// Unlike the dedup mirror, the projection commits this mirror <em>after</em> the
/// row write has drained, so membership implies the row is readable.
/// </summary>
sealed class CorrelationRingMirror
{
    readonly HashSet<Guid> _set = [];
    Guid[] _ring = [];
    int _head;
    int _count;

    /// <summary>
    /// Rebuilds the in-memory set from the persisted ring. Copies the populated
    /// slot range so the mirror's head/count stays in lockstep with the canonical
    /// state, then populates the set from the <paramref name="count"/> populated
    /// slots only.
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
            for (var i = 0; i < count; i++)
            {
                _set.Add(_ring[i]);
            }
        }
    }

    public bool Contains(Guid correlationId) => _set.Contains(correlationId);

    /// <summary>
    /// Records a commit at the ring's current head position. When the ring is
    /// full, the id previously held at <c>head</c> is evicted from the set before
    /// the new id is added. Mirrors the caller's persisted-ring update; call once
    /// after the row write has drained.
    /// </summary>
    public void Commit(Guid correlationId)
    {
        if (_count == _ring.Length)
        {
            var displaced = _ring[_head];
            _set.Remove(displaced);
        }

        _ring[_head] = correlationId;
        _head = (_head + 1) % _ring.Length;

        if (_count < _ring.Length)
        {
            _count++;
        }

        _set.Add(correlationId);
    }
}
