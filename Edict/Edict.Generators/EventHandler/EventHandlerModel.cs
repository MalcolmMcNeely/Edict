namespace Edict.Generators.EventHandler;

internal sealed record EventHandlerGrainModel(
    string Namespace,
    string GrainName,
    EquatableArray<EventHandlerHandlerModel> Handlers);

internal sealed record EventHandlerHandlerModel(
    string EventFqn,
    string EventSimpleName,
    string StreamName);
