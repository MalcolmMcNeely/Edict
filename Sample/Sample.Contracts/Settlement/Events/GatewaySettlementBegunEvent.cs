using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Settlement.Events;

/// <summary>
/// Raised when a gateway settlement begins; the saga reacts by scheduling its
/// poll. <c>PollTimeoutSeconds</c> rides through from the start command so the
/// saga can cap (or leave uncapped) its polling schedule.
/// </summary>
[EdictStream("Settlement")]
public sealed partial record GatewaySettlementBegunEvent(
    [property: EdictRouteKey] Guid PaymentId,
    int PollTimeoutSeconds) : EdictEvent;
