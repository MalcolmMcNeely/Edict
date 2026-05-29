using Edict.Generators.Commands;

namespace Edict.Generators.EventTagWriters;

internal sealed record EventTagWritersModel(
    string Fqn,
    EquatableArray<TelemeterizedProperty> TelemeterizedProperties);
