namespace Edict.Core.DeadLetter;

/// <summary>
/// String-marker type used by <c>DeadLetterPromoter</c> when an outbox entry
/// carries an effect kind the drain can't dispatch (an enum value added
/// post-deploy, a corrupted state row). The promoter never throws this — it
/// writes the type's name into the dead-letter row's <c>ExceptionType</c>
/// column so operator filtering by exception type stays uniform across
/// throwable runtime faults and promoter-emitted synthetic causes.
/// </summary>
public sealed class EdictUnsupportedEffectKindException : Exception
{
    public EdictUnsupportedEffectKindException(string message)
        : base(message)
    {
    }
}
