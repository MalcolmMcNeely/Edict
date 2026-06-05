using Edict.Contracts.Commands;

namespace Sample.Contracts.Settlement.Commands;

/// <summary>Issued by the saga when the gateway poll settles successfully.</summary>
public sealed partial record ConfirmSettlementCommand(
    [property: EdictRouteKey] Guid PaymentId) : EdictCommand;
