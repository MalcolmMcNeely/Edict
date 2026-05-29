namespace Edict.Core.DeadLetter;

/// <summary>
/// String-marker type used by <c>DeadLetterPromoter</c> when a SendCommand
/// effect references a command type with no <c>[EdictRouteKey]</c> property.
/// The promoter never throws this — it writes the type's name into the
/// dead-letter row's <c>ExceptionType</c> column.
/// </summary>
public sealed class EdictMissingRouteKeyException : Exception
{
    public EdictMissingRouteKeyException(string message)
        : base(message)
    {
    }
}
