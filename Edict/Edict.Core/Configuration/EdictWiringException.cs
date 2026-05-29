namespace Edict.Core.Configuration;

/// <summary>
/// Single typed exception for all Edict wiring faults — conditions knowable
/// before the silo runs (missing client, missing options, missing provider
/// marker, duplicate registrar). Thrown either from the offending
/// <c>AddEdict*</c> extension call site or aggregated by
/// <see cref="EdictWiringValidator"/> at host start.
/// <para>
/// Never thrown from a <c>Handle</c> call. Runtime faults — anything that
/// can only surface once a message is in flight — use per-cause-narrative
/// <c>Edict*</c> exception types so the dead-letter classifier and consumer
/// catches can discriminate by type. Wiring faults pre-date the dead-letter
/// pipeline, so the aggregated problem list matches a single-type-with-
/// message shape.
/// </para>
/// </summary>
public sealed class EdictWiringException : Exception
{
    public EdictWiringException(string message)
        : base(message)
    {
    }

    public EdictWiringException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
