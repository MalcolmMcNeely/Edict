namespace Edict.Generators.Commands;

internal sealed record CommandModel(
    string Fqn,
    string SimpleName,
    string Namespace,
    string RouteKeyProperty,
    string RouteKeyStringification,
    bool IsTenantScoped,
    EquatableArray<TelemeterizedProperty> TelemeterizedProperties);

internal sealed record CommandHandlerGrainModel(
    string Namespace,
    string GrainName,
    string GrainTypeName,
    string GrainFqn,
    EquatableArray<CommandModel> Commands,
    EquatableArray<ScheduleMessageModel> ScheduleMessages,
    EquatableArray<ScheduleMessageModel> ScheduleTimeoutMessages);

internal sealed record ScheduleMessageModel(string Fqn, string SimpleName, string Namespace);

internal sealed record TelemeterizedProperty(string PropertyName);
