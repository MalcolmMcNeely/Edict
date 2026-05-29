namespace Edict.Generators.Classification;

/// <summary>
/// The kind of Edict-shaped type a node is, as resolved by
/// <see cref="EdictTypeClassifier"/>. Single source of truth for
/// base-class-driven discovery.
/// </summary>
public enum EdictTypeKind
{
    None,
    Command,
    Event,
    CommandHandler,
    EventHandler,
    ProjectionBuilder,
    Saga,
}
