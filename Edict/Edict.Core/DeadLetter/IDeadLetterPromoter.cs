using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

interface IDeadLetterPromoter
{
    OutboxEntry Promote(
        OutboxEntry failed,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset now);
}
