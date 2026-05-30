namespace Edict.Core.DeadLetter;

sealed class EdictUnsupportedEffectKindException : Exception
{
    public EdictUnsupportedEffectKindException(string message)
        : base(message)
    {
    }
}
