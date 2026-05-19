namespace Edict.Core.Saga;

/// <summary>
/// Orleans grain-interface root for every saga. The
/// <see cref="EdictSagaGenerator"/>-emitted <c>I{Saga}</c> partial interface
/// derives from this, mirroring <c>IEdictProjectionBuilder</c> for the
/// projection role (ADR 0020). Consumer-facing surface, so brand-prefixed.
/// </summary>
public interface IEdictSaga : IGrainWithGuidKey;
