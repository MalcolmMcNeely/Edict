namespace Edict.Core.Projections;

/// <summary>
/// Thrown when a projection that keeps no readable row store (the abstract
/// <see cref="EdictProjectionBuilder"/> root, e.g. a projection that exposes its
/// state through a bespoke grain interface) is asked to serve a row read. The
/// read facade only routes row types owned by a List projection, so this is a
/// direct-call programming error, never reached through
/// <c>IEdictProjectionReader</c>.
/// </summary>
public sealed class EdictUnsupportedProjectionReadException(Type projectionType)
    : InvalidOperationException(
        $"Projection '{projectionType.FullName}' has no readable row store. " +
        "Only an EdictListProjectionBuilder<TRow> can serve IEdictProjectionReader reads.")
{
    /// <summary>The projection type that cannot serve a row read.</summary>
    public Type ProjectionType { get; } = projectionType;
}
