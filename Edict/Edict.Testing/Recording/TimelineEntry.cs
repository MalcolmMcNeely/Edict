namespace Edict.Testing.Recording;

/// <summary>
/// One observable step in a workflow, captured by the Test Framework and
/// rendered into the single Verify-shaped <see cref="Timeline"/>. Volatile
/// envelope fields (ids, timestamps, W3C trace context) are deliberately
/// excluded so the snapshot is the deterministic ADR 0007 drift guard, not
/// noise — only the domain payload survives.
/// </summary>
public sealed record TimelineEntry(
    string Kind,
    string Type,
    IReadOnlyDictionary<string, object?> Data);
