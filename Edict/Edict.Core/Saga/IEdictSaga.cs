using Edict.Core.Idempotency;

namespace Edict.Core.Saga;

/// <summary>
/// Orleans grain-interface root for every saga. The
/// <see cref="EdictSagaGenerator"/>-emitted <c>I{Saga}</c> partial interface
/// derives from this, mirroring <c>IEdictProjectionBuilder</c> for the
/// projection role (ADR 0020). Consumer-facing surface, so brand-prefixed.
/// <para>
/// <see cref="GetEdictProgressAsync"/> is the framework's typed-state probe
/// for tests (and operator inspection): every saga exposes its durable
/// <c>Progress</c> through this single method, implemented once on
/// <see cref="EdictSaga{TProgress}"/>. The boxing keeps the interface
/// non-generic so it can live on the consumer-typed brand root.
/// </para>
/// </summary>
public interface IEdictSaga : IEdictEventConsumer
{
    Task<object> GetEdictProgressAsync();
}
