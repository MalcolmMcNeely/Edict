namespace Edict.Contracts.Configuration;

/// <summary>
/// Tag interface: each provider extension (streams, persistence) registers an
/// implementation in DI so <see cref="WiringInspector"/> can detect a missing
/// provider call at startup. Lives in the shared kernel so a future
/// non-Azure provider package can register its own marker without depending on
/// <c>Edict.Core</c>.
/// </summary>
public interface IEdictWiringMarker
{
}

/// <summary>Registered by <c>AddEdictAzureStreams</c> (and any future streams provider).</summary>
public sealed class EdictStreamsProviderMarker : IEdictWiringMarker
{
}

/// <summary>Registered by <c>AddEdictAzurePersistence</c> (and any future persistence provider).</summary>
public sealed class EdictPersistenceProviderMarker : IEdictWiringMarker
{
}
