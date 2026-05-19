namespace Edict.Core;

/// <summary>
/// Thrown when a command reaches a grain whose DeadLetter slice has hit its
/// configured cap (ADR 0019). This is an <b>infrastructure fault</b>, not a
/// business rejection — block-intake exists so nothing is silently dropped, so
/// the caller sees a thrown exception rather than an
/// <see cref="Contracts.Commands.EdictCommandResult.Rejected"/>. Recovery is an
/// operator redrive; the grain self-clears once the dead-lettered entries leave
/// the slice.
/// <para>
/// Lives in <c>Edict.Core</c>, not the Orleans-free <c>Edict.Contracts</c>
/// (ADR 0005/0008): an exception thrown across the grain→caller wire needs an
/// Orleans codec, so it must carry <c>[GenerateSerializer]</c> — which
/// <c>Edict.Contracts</c> cannot. It is still consumer-observable and therefore
/// brand-prefixed (ADR 0017). Persisted? No — a wire fault, so an
/// <c>[Alias]</c> via <c>nameof</c> like the command wire types (ADR 0010);
/// ORLEANS0010 is never suppressed.
/// </para>
/// </summary>
[GenerateSerializer]
[Alias(nameof(EdictOutboxSaturatedException))]
public sealed class EdictOutboxSaturatedException : Exception
{
    public EdictOutboxSaturatedException()
        : base("The aggregate's dead-letter slice is at capacity; intake is blocked until an operator redrives.")
    {
    }

    public EdictOutboxSaturatedException(string message)
        : base(message)
    {
    }
}
