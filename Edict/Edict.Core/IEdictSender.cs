using Edict.Abstractions;

namespace Edict.Core;

/// <summary>
/// The DI-injected entry point a consumer uses to issue a command. A single
/// method, no static or extension form and no per-command overloads, because
/// this is the substitution seam <c>Edict.Testing</c> later swaps for an
/// in-memory implementation so consumer code is identical under test and in
/// production.
/// </summary>
public interface IEdictSender
{
    /// <summary>Routes <paramref name="command"/> to its aggregate grain and
    /// returns the handler's outcome.</summary>
    Task<CommandResult> Send(Command command);
}
