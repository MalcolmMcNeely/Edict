namespace Edict.Generators.Sagas;

internal sealed record SagaGrainModel(
    string Namespace,
    string GrainName,
    EquatableArray<SagaHandlerModel> Handlers);

internal sealed record SagaHandlerModel(
    string EventFqn,
    string EventSimpleName,
    string StreamName);
