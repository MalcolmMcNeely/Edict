namespace Edict.Tests.Conformance.Idempotency;

/// <summary>
/// Conformance fixture base for the window-size scenarios: the silo is wired
/// with a non-default <c>EdictOptions.IdempotencyWindowSize</c> so the scenario
/// can prove the option threads through DI to the base. Each provider's
/// subclass owns substrate bring-up plus the option configuration.
/// </summary>
public abstract class IdempotencyWindowSizeFixture : ConformanceFixture
{
    public abstract int ConfiguredWindowSize { get; }
}
