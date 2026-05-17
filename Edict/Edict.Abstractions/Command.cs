namespace Edict.Abstractions;

/// <summary>
/// Base for an expression of intent to change state, addressed to exactly one
/// aggregate grain via a direct grain call. Concrete commands derive from this.
/// Carries only a framework-assigned <see cref="CommandId"/>; it deliberately
/// holds no trace-correlation fields because a direct grain call propagates
/// <see cref="System.Diagnostics.Activity"/> context natively (ADR 0003).
/// </summary>
public abstract record Command
{
    /// <summary>Framework-assigned identity for this command instance.</summary>
    public Guid CommandId { get; init; }
}
