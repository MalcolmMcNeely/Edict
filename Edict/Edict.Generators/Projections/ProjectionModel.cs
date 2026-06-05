namespace Edict.Generators.Projections;

internal sealed record ProjectionGrainModel(
    string Namespace,
    string GrainName,
    string GrainTypeName,
    string? RowFqn,
    EquatableArray<ProjectionHandlerModel> Handlers);

internal sealed record ProjectionHandlerModel(
    string EventFqn,
    string EventSimpleName,
    string StreamName);
