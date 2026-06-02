namespace Edict.Contracts.Configuration;

/// <summary>
/// Silo-wide saga knobs, sibling to <see cref="EdictOptions"/> and tuned the
/// same way: a property with a default, a <c>ValidateOnStart</c> rule, and a
/// line in <c>Sample.Azure.Silo/Program.cs</c>. Brand-prefixed because the
/// consumer types it. Configured under the existing <c>AddEdict()</c>.
/// </summary>
public sealed class EdictSagaOptions
{
    /// <summary>
    /// The absolute lifetime cap applied to any saga that does not declare its
    /// own <c>[EdictSagaTimeout]</c>. Ships finite at 7 days so no saga can sit
    /// forever by accident, even one nobody annotated. Set to <c>null</c> to
    /// return the silo to fully opt-in behaviour (no saga is capped unless it
    /// carries the attribute). A per-saga <c>[EdictSagaTimeout(Unbounded = true)]</c>
    /// always beats this default — opting out is the saga author's call.
    /// </summary>
    public TimeSpan? DefaultTimeout { get; set; } = TimeSpan.FromDays(7);
}
