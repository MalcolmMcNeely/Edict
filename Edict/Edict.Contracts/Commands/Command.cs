using MessagePack;

namespace Edict.Contracts.Commands;

/// <summary>
/// Base for an expression of intent to change state, addressed to exactly one
/// aggregate grain via a direct grain call. Concrete commands derive from this.
/// Carries a framework-assigned <see cref="CommandId"/> and a chain-stable
/// <see cref="CorrelationId"/>; it deliberately holds no trace-correlation
/// fields because a direct grain call propagates
/// <see cref="System.Diagnostics.Activity"/> context natively.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public abstract record EdictCommand
{
    /// <summary>Framework-assigned identity for this command instance.</summary>
    public Guid CommandId { get; init; }

    /// <summary>
    /// Chain-stable correlation id identifying the whole conversation this
    /// Command belongs to. Framework-stamped, never threaded by hand: minted in
    /// <c>EdictSender.SendAsync</c> when empty, honoured when a caller supplied
    /// one (idempotency-key style), and carried unchanged across the Command to
    /// Event to Command chain so a read-your-writes cursor survives even a Saga
    /// in the middle.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
