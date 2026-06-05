using Edict.Generators.Commands;

namespace Edict.Generators.Sagas;

internal sealed record SagaGrainModel(
    string Namespace,
    string GrainName,
    EquatableArray<SagaHandlerModel> Handlers,
    EquatableArray<ScheduleMessageModel> ScheduleMessages,
    EquatableArray<ScheduleMessageModel> ScheduleTimeoutMessages);

internal sealed record SagaHandlerModel(
    string EventFqn,
    string EventSimpleName,
    string StreamName);
