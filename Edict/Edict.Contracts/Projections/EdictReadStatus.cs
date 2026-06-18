namespace Edict.Contracts.Projections;

/// <summary>
/// How a Projection read resolved against an optional read-your-writes cursor.
/// Eventual-consistency lag is an expected outcome, so it surfaces here as a
/// status rather than a thrown exception.
/// </summary>
public enum EdictReadStatus
{
    /// <summary>No cursor was supplied: the read answered from the current store state immediately (the poll path).</summary>
    Immediate = 0,

    /// <summary>A cursor was supplied and the named conversation's first effect on this projection is visible.</summary>
    CursorReached = 1,

    /// <summary>
    /// A cursor was supplied but the wait elapsed before the conversation became
    /// visible. The latest available row is still returned, flagged so the
    /// consumer chooses what to do with possibly-stale data.
    /// </summary>
    CursorTimedOut = 2,
}
