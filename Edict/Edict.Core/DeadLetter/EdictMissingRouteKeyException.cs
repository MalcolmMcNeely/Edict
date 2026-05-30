namespace Edict.Core.DeadLetter;

sealed class EdictMissingRouteKeyException : Exception
{
    public EdictMissingRouteKeyException(string message)
        : base(message)
    {
    }
}
