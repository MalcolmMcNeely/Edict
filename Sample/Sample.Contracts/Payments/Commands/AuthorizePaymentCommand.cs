using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Payments.Commands;

public sealed partial record AuthorizePaymentCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    decimal Amount) : EdictCommand;
