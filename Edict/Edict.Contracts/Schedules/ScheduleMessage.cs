using MessagePack;

namespace Edict.Contracts.Schedules;

/// <summary>
/// Base for a serializable message that drives a recurring schedule started from
/// inside a Command Handler's <c>HandleAsync</c>. A schedule persists this
/// message (its domain data plus its <c>[Alias]</c> type identity), never a
/// delegate, so it survives grain deactivation. Each fire deserializes it and
/// routes it back through the handler's generated dispatch, re-entering the full
/// handler lifecycle. Concrete messages derive from this and carry only their
/// own domain payload; the handler reads everything else from durable
/// <c>State</c>.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public abstract record EdictScheduleMessage;
