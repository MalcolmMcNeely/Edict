namespace Edict.Core.DeadLetter;

/// <summary>
/// Stable public anchor for the dead-letter table's name. Consumers wire their
/// admin-side <see cref="Contracts.TableStorage.IEdictTableRepository{T}"/> via
/// <c>EdictDeadLetterTable.Name</c> so they never reference the framework's
/// projection-builder grain class.
/// </summary>
public static class EdictDeadLetterTable
{
    /// <summary>The literal name of the table every dead-letter row is written to.</summary>
    public const string Name = "deadletter";
}
