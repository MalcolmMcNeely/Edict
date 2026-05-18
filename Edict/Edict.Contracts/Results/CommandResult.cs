using MessagePack;

namespace Edict.Contracts.Results;

/// <summary>
/// The outcome envelope a Command Handler returns. A closed hierarchy of
/// exactly <see cref="Accepted"/> and <see cref="Rejected"/> — consumers
/// exhaustively <c>switch</c> on it. Business rejection is a first-class
/// outcome here, never a thrown exception; infrastructure faults still throw.
/// The private constructor makes the hierarchy closed: only the nested
/// variants can derive from it.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public abstract record EdictCommandResult
{
    private EdictCommandResult()
    {
    }

    /// <summary>The command was accepted and handled. Carries no domain data.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record Accepted : EdictCommandResult;

    /// <summary>The command was rejected for one or more business reasons.</summary>
    /// <param name="Reasons">The structured reasons the command was rejected.</param>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record Rejected(IReadOnlyList<EdictRejectionReason> Reasons) : EdictCommandResult;
}
