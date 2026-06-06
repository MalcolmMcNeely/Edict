namespace Edict.Contracts.Projections;

/// <summary>
/// The result of a point Projection read: the value read (a single row for a List
/// projection, or the whole read model for an in-grain State projection; or
/// <see langword="null"/> when absent) plus the <see cref="EdictReadStatus"/>
/// describing how it resolved against an optional read-your-writes cursor. A read
/// never throws for eventual-consistency lag; a
/// <see cref="EdictReadStatus.CursorTimedOut"/> read still returns the latest
/// available value so the consumer decides what to do with it.
/// </summary>
/// <param name="Value">The value read, or <see langword="null"/> when none exists at the key.</param>
/// <param name="Status">How the read resolved against the supplied cursor.</param>
public readonly record struct EdictProjectionRead<TValue>(TValue? Value, EdictReadStatus Status)
    where TValue : class;
