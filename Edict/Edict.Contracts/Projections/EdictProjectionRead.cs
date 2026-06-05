namespace Edict.Contracts.Projections;

/// <summary>
/// The result of a point Projection read: the row (or <see langword="null"/> when
/// absent) plus the <see cref="EdictReadStatus"/> describing how it resolved
/// against an optional read-your-writes cursor. A read never throws for
/// eventual-consistency lag; a <see cref="EdictReadStatus.CursorTimedOut"/> read
/// still returns the latest available row so the consumer decides what to do with it.
/// </summary>
/// <param name="Row">The row read, or <see langword="null"/> when no row exists at the key.</param>
/// <param name="Status">How the read resolved against the supplied cursor.</param>
public readonly record struct EdictProjectionRead<TRow>(TRow? Row, EdictReadStatus Status)
    where TRow : class;
