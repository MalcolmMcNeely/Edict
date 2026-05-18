using MessagePack;

namespace Edict.Contracts.Results;

/// <summary>
/// A single structured reason a Command Handler rejected a command. The
/// <paramref name="Code"/> is stable and machine-branchable; the
/// <paramref name="Message"/> is human-facing display text.
/// </summary>
/// <param name="Code">Stable identifier a UI can branch on.</param>
/// <param name="Message">Human-readable explanation for display.</param>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EdictRejectionReason(string Code, string Message);
