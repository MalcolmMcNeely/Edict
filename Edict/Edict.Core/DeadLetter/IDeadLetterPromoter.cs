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

    /// <summary>
    /// Builds the synthetic dead-letter row for a Command Handler schedule that
    /// hit its timeout cap with no <c>OnScheduleTimeoutAsync</c> hook. There is no
    /// failing effect to promote, so this is built directly rather than from an
    /// <see cref="OutboxEntry"/>; it never throws (it sits on the safety net that
    /// cannot throw) and stamps the <c>EdictScheduleTimeoutException</c> marker name
    /// without instantiating it.
    /// </summary>
    OutboxEntry PromoteScheduleTimeout(
        string scheduleMessageType,
        string sourceGrainKey,
        string sourceGrainType,
        string? traceParent,
        string? traceState,
        DateTimeOffset now);
}
