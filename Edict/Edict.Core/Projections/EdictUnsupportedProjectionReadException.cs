using Orleans;

namespace Edict.Core.Projections;

/// <summary>
/// Thrown when a projection species is asked for a read it does not serve: a List
/// projection asked for a whole-payload read, an in-grain State projection asked
/// for a row read, or the abstract root asked for any read. Both reader facades
/// resolve the grain through the one read-route map, so injecting the wrong
/// species' reader for a projection type resolves the grain but surfaces here at
/// runtime rather than returning silent wrong data. It is raised inside the
/// projection grain and crosses back to the reader, so it carries an Orleans
/// serializer.
/// </summary>
[GenerateSerializer]
public sealed class EdictUnsupportedProjectionReadException : InvalidOperationException
{
    /// <summary>Creates the exception for the projection type that cannot serve the read.</summary>
    public EdictUnsupportedProjectionReadException(Type projectionType)
        : base(
            $"Projection '{projectionType.FullName}' does not serve this read. " +
            "Read an EdictListProjectionBuilder<TListProjection> through IEdictListProjectionReader " +
            "and an EdictProjectionBuilder<TProjection> through IEdictProjectionReader.")
    {
        ProjectionTypeName = projectionType.FullName ?? projectionType.Name;
    }

    /// <summary>The full name of the projection type that cannot serve the requested read.</summary>
    [Id(0)]
    public string ProjectionTypeName { get; }
}
