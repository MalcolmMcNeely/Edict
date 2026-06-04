using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Cart.Events;

[EdictStream("Cart")]
public sealed partial record CartCheckedOutEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid CartId,
    int ItemCount,
    IReadOnlyList<string> Skus) : EdictEvent;
