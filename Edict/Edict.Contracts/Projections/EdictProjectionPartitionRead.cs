namespace Edict.Contracts.Projections;

/// <summary>
/// The result of a partition Projection read: every row in the partition plus the
/// <see cref="EdictReadStatus"/> describing how it resolved against an optional
/// read-your-writes cursor. The same cursor semantics as a point read apply: the
/// rows reflect the cursor's correlation once
/// <see cref="EdictReadStatus.CursorReached"/>, and a
/// <see cref="EdictReadStatus.CursorTimedOut"/> read still returns the latest
/// available rows.
/// </summary>
/// <param name="Rows">Every row in the partition (empty when none exist).</param>
/// <param name="Status">How the read resolved against the supplied cursor.</param>
public readonly record struct EdictProjectionPartitionRead<TRow>(IReadOnlyList<TRow> Rows, EdictReadStatus Status)
    where TRow : class;
