namespace Edict.Testing.Recording;

/// <summary>
/// The single Verify-shaped view of a whole workflow: dispatched Commands,
/// raised Events, Saga <c>Progress</c>, projection rows and dead-letters in the
/// order the in-memory engine produced them. A test asserts one whole workflow
/// with <c>await Verify(app.Timeline)</c> — one snapshot, no Assert chain
/// (ADR 0016 / repo testing convention).
/// </summary>
public sealed record Timeline(IReadOnlyList<TimelineEntry> Entries);
