namespace Edict.Core.DeadLetter;

/// <summary>
/// Stable anchor for the dead-letter table's name — the partition every
/// dead-letter row is written to and the singleton dead-letter projection grain
/// reads back through <see cref="Contracts.DeadLetter.IEdictDeadLetterRepository"/>.
/// </summary>
public static class EdictDeadLetterTable
{
    /// <summary>The literal name of the table every dead-letter row is written to.</summary>
    public const string Name = "deadletter";
}
