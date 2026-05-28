namespace Edict.Generators.Commands;

internal sealed record CommandModel(
    string Fqn,
    string SimpleName,
    string Namespace,
    string RouteKeyProperty,
    EquatableArray<TelemeterizedProperty> TelemeterizedProperties);

internal sealed record CommandHandlerGrainModel(
    string Namespace,
    string GrainName,
    string GrainTypeName,
    string GrainFqn,
    EquatableArray<CommandModel> Commands);

internal sealed record TelemeterizedProperty(string PropertyName);
