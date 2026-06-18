using MessagePack;

namespace Edict.Contracts.Commands;

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
    EdictCommandResult()
    {
    }

    /// <summary>
    /// The command was accepted and handled. Carries the framework-stamped
    /// <see cref="EdictCursor"/> for the work the command set in motion; a
    /// consumer feeds it to a read-your-writes Projection read. The cursor is
    /// stamped by the runtime after the handler returns, so a consumer handler
    /// keeps writing <c>new EdictCommandResult.Accepted()</c> and never threads
    /// the conversation id by hand.
    /// </summary>
    /// <param name="Cursor">The read-your-writes cursor for this command's chain.</param>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record Accepted(EdictCursor Cursor = default) : EdictCommandResult;

    /// <summary>The command was rejected for one or more business reasons.</summary>
    /// <param name="Reasons">The structured reasons the command was rejected.</param>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record Rejected(IReadOnlyList<EdictRejectionReason> Reasons) : EdictCommandResult;
}
