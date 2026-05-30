// Synthetic Edict base stubs. The fixture project does not reference the real
// Edict packages — the HandlerScanner resolves base types by their metadata
// names, so a same-named stub here is sufficient to drive an end-to-end scan.

namespace Edict.Contracts.Persistence
{
    public interface IEdictPersistedState { }
}

namespace Edict.Contracts.Commands
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public sealed class EdictRouteKeyAttribute : System.Attribute { }
    public abstract record EdictCommand;
}

namespace Edict.Contracts.Events
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class EdictStreamAttribute : System.Attribute
    {
        public EdictStreamAttribute(string name) { Name = name; }
        public string Name { get; }
    }
    public abstract record EdictEvent;
}

namespace Edict.Core.Commands
{
    using Edict.Contracts.Persistence;
    public abstract class EdictCommandHandler<TState> where TState : IEdictPersistedState, new() { }
    public sealed class EdictUnit : IEdictPersistedState { }
    public abstract class EdictCommandHandler : EdictCommandHandler<EdictUnit> { }
}

namespace Edict.Core.EventHandler
{
    public abstract class EdictEventHandler { }
}

namespace Edict.Core.Sagas
{
    using Edict.Contracts.Persistence;
    public abstract class EdictSaga<TProgress> where TProgress : IEdictPersistedState, new() { }
}

namespace Edict.Core.Projections
{
    using Edict.Contracts.Persistence;
    public abstract class EdictProjectionBuilder { }
    public abstract class EdictTableProjectionBuilder<T> : EdictProjectionBuilder where T : class, IEdictPersistedState, new() { }
}
