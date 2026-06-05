using Edict.Contracts.Commands;

namespace Sample.Contracts.Settlement.Commands;

/// <summary>
/// Starts a gateway settlement workflow for a payment. <c>PollTimeoutSeconds</c>
/// caps the saga's polling schedule; zero leaves it bounded only by the saga's
/// own lifetime.
/// </summary>
public sealed partial record BeginGatewaySettlementCommand(
    [property: EdictRouteKey] Guid PaymentId,
    int PollTimeoutSeconds) : EdictCommand;
