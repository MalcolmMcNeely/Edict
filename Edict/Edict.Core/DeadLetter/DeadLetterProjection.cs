using Edict.Contracts.DeadLetter;
using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Projects the internal persisted <see cref="OutboxSlice.DeadLetter"/> list
/// onto the consumer-facing read DTO (ADR 0019). The grain is the only source
/// of truth for dead-letters (no external store), so both grain roots project
/// through this one pure mapping. Bare-named — no consumer types it.
/// </summary>
static class DeadLetterProjection
{
    public static IReadOnlyList<EdictDeadLetterEntry> From(OutboxSlice slice) =>
        slice.DeadLetter
            .Select(static dead => new EdictDeadLetterEntry
            {
                EntryId = dead.Entry.EntryId,
                Kind = dead.Entry.Kind.ToString(),
                AttemptCount = dead.Entry.AttemptCount,
                DeadLetteredAt = dead.DeadLetteredAt,
                Reason = dead.Reason,
            })
            .ToList();
}
