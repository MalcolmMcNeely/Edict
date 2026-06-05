namespace Edict.Core.Projections;

/// <summary>
/// Pure routing core for projection reads: given a read-model row type, returns
/// the projection grain class name used to disambiguate the many grain classes
/// that share the <see cref="IEdictProjectionBuilder"/> interface. The map is
/// generator-fed (one entry per <c>EdictListProjectionBuilder&lt;TRow&gt;</c>),
/// mirroring the command-side <c>CommandRouteResolver</c>; no Orleans dependency,
/// so it stays unit-testable without a cluster.
/// </summary>
internal sealed class ProjectionReadRouteResolver(IReadOnlyDictionary<Type, string> routes)
{
    /// <summary>Returns the projection grain class name that owns <paramref name="rowType"/>.</summary>
    public string Resolve(Type rowType)
    {
        ArgumentNullException.ThrowIfNull(rowType);

        return routes.TryGetValue(rowType, out var grainClassName)
            ? grainClassName
            : throw new EdictUnreadableProjectionException(rowType);
    }
}
