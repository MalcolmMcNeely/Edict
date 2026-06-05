namespace Edict.Core.Projections;

/// <summary>
/// Thrown when a read-model row type has no generator-emitted projection route —
/// i.e. no <c>EdictListProjectionBuilder&lt;TRow&gt;</c> declares it, or the
/// owning assembly was not passed to <c>AddEdict()</c>. This is a wiring fault,
/// not a business outcome, so it throws rather than returning an empty read.
/// </summary>
public sealed class EdictUnreadableProjectionException(Type rowType)
    : InvalidOperationException(
        $"No projection builder owns read-model row '{rowType.FullName}'. " +
        $"Declare an EdictListProjectionBuilder<{rowType.Name}> and register its " +
        "assembly with AddEdict().")
{
    /// <summary>The row type that could not be routed.</summary>
    public Type RowType { get; } = rowType;
}
