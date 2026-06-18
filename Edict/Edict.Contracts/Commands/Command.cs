using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;

using MessagePack;

namespace Edict.Contracts.Commands;

/// <summary>
/// Base for an expression of intent to change state, addressed to exactly one
/// aggregate grain via a direct grain call. Concrete commands derive from this.
/// Carries a framework-assigned <see cref="CommandId"/> and a chain-stable
/// <see cref="ConversationId"/>; it deliberately holds no trace-correlation
/// fields because a direct grain call propagates
/// <see cref="System.Diagnostics.Activity"/> context natively.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public abstract record EdictCommand
{
    /// <summary>Framework-assigned identity for this command instance.</summary>
    public Guid CommandId { get; init; }

    /// <summary>
    /// Chain-stable conversation id naming the whole conversation this
    /// Command belongs to. Framework-stamped, never threaded by hand: minted in
    /// <c>EdictSender.SendAsync</c> when empty, honoured when a caller supplied
    /// one (idempotency-key style), and carried unchanged across the Command to
    /// Event to Command chain so a read-your-writes cursor survives even a Saga
    /// in the middle.
    /// </summary>
    public Guid ConversationId { get; init; }

    /// <summary>
    /// The actor on whose authority this Command was issued. Framework-stamped,
    /// never threaded by hand: resolved from the edge at the originating
    /// <c>SendAsync</c> when auditing is on, or supplied explicitly through the
    /// <c>SendAsync(command, principal)</c> escape hatch, then carried unchanged
    /// across the whole Command to Event chain. Null until stamped, and null
    /// stays null when auditing is off.
    /// </summary>
    public EdictPrincipal? Principal { get; init; }

    /// <summary>
    /// The tenant whose data this Command belongs to: the company wall, not the
    /// acting <see cref="Principal"/>. Framework-stamped, never threaded by hand:
    /// resolved from the edge at the originating <c>SendAsync</c> when tenancy is
    /// on, or supplied explicitly through the <c>SendAsync(command, tenant)</c>
    /// establishing-crossing overload, then carried unchanged across the whole
    /// Command to Event chain. Null until stamped, and null stays null for a
    /// single-tenant app that registered no tenant resolver.
    /// </summary>
    public EdictTenantId? Tenant { get; init; }
}
