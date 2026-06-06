namespace Edict.Core.Projections;

/// <summary>
/// Pure routing core for projection reads: given a read-model type, returns the
/// projection grain class name used to disambiguate the many grain classes that
/// share the <see cref="IEdictProjectionBuilder"/> interface. Both reader facades
/// resolve through this one map; it is generator-fed (one entry per species — the
/// row type for an <c>EdictListProjectionBuilder&lt;TListProjection&gt;</c>, the
/// projection type for an <c>EdictProjectionBuilder&lt;TProjection&gt;</c>),
/// mirroring the command-side <c>CommandRouteResolver</c>; no Orleans dependency,
/// so it stays unit-testable without a cluster.
/// </summary>
internal sealed class ProjectionReadRouteResolver(IReadOnlyDictionary<Type, string> routes)
{
    /// <summary>Returns the projection grain class name that owns <paramref name="readModelType"/>.</summary>
    public string Resolve(Type readModelType)
    {
        ArgumentNullException.ThrowIfNull(readModelType);

        return routes.TryGetValue(readModelType, out var grainClassName)
            ? grainClassName
            : throw new EdictUnreadableProjectionException(readModelType);
    }
}
