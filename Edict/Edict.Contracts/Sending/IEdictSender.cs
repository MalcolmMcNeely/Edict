using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Contracts.Tenancy;

namespace Edict.Contracts.Sending;

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
    Task<EdictCommandResult> SendAsync(EdictCommand command);

    /// <summary>
    /// Escape hatch for a context-free origin (worker, import, admin script,
    /// test) where no edge resolver can supply the actor: stamps
    /// <paramref name="principal"/> onto <paramref name="command"/> directly, so
    /// the send is attributed without tripping the auditing fail-closed gate. The
    /// consumer mints their own principal (e.g. <c>EdictPrincipal.Of("system")</c>);
    /// Edict ships no system sentinel.
    /// </summary>
    Task<EdictCommandResult> SendAsync(EdictCommand command, EdictPrincipal principal);

    /// <summary>
    /// The explicit establishing-crossing overload, and the escape hatch for a
    /// context-free origin: stamps <paramref name="tenant"/> onto
    /// <paramref name="command"/> directly, so a public-to-tenant send establishes
    /// the wall without tripping the tenancy fail-closed gate. The consumer maps
    /// their own tenant (e.g. <c>EdictTenantId.Of(companyId)</c>) from a trusted
    /// source, never from the request body.
    /// </summary>
    Task<EdictCommandResult> SendAsync(EdictCommand command, EdictTenantId tenant);
}
