namespace Edict.Core.Projections;

// Per-invocation row holder for EdictTableProjectionBuilder<T>: the dispatch
// loads the row into the box, the handler reads/replaces CurrentRow through it,
// and the dispatch reads the computed row back to stage the UpsertRow effect.
// A box (not the row itself) because the loaded row replaces the freshly-opened
// scope's value, which an InvocationScope state instance cannot do in place.
sealed class ProjectionRowBox<T>
    where T : class, new()
{
    public T Row { get; set; } = default!;
}
