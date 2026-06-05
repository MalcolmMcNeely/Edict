using Edict.Contracts.Commands;

namespace Sample.Contracts.Settlement.Commands;

/// <summary>
/// Issued by the saga's schedule-timeout compensation when the gateway never
/// settles within the poll cap.
/// </summary>
public sealed partial record AbandonSettlementCommand(
    [property: EdictRouteKey] Guid PaymentId) : EdictCommand;
