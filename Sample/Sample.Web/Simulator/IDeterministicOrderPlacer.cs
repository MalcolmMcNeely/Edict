namespace Sample.Web.Simulator;

/// <summary>
/// Demo-prop seam for placing a single fixed-shape order — three lines, fixed
/// SKUs, sub-threshold amount — so a stranger can follow one clean trace tree
/// in Aspire instead of grepping through interleaved simulator traffic.
/// </summary>
public interface IDeterministicOrderPlacer
{
    /// <summary>Mints an order id, dispatches Place + 3× AddLineItem + Submit, and returns the id for spotlight tracking.</summary>
    Task<Guid> FireOneAsync(CancellationToken cancellationToken = default);
}
