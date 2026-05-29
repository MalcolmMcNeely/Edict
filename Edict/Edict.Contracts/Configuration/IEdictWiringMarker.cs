using System.ComponentModel;

namespace Edict.Contracts.Configuration;

// Tag interface: each provider extension (streams, persistence) registers an
// implementation in DI so EdictWiringInspector can detect a missing provider
// call at startup. Lives in the shared kernel so a future non-Azure provider
// package can register its own marker without depending on Edict.Core. Public
// so provider extensions can register concrete subclasses across assembly
// boundaries; hidden from consumer IntelliSense because nothing in user code
// names it.
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEdictWiringMarker
{
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EdictStreamsProviderMarker : IEdictWiringMarker
{
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EdictPersistenceProviderMarker : IEdictWiringMarker
{
}
