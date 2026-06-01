namespace Edict.Core.DeadLetter;

sealed class EdictPromotionSerializationException : Exception
{
    public EdictPromotionSerializationException(string message)
        : base(message)
    {
    }
}
