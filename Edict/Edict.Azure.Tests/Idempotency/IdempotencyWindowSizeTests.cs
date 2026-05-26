using Edict.Tests.Conformance.Idempotency;

namespace Edict.Azure.Tests.Idempotency;

/// <summary>
/// Azurite/Testcontainers binding for
/// <see cref="IdempotencyWindowSizeScenarios{TFixture}"/>. Inherits the
/// scenarios from <c>Edict.Tests.Conformance</c>; the two [Fact]s run unmodified
/// against the per-test <see cref="IdempotencyWindowSizeClusterFixture"/>.
/// </summary>
[Collection(IdempotencyWindowSizeClusterCollection.Name)]
public sealed class IdempotencyWindowSizeTests
    : IdempotencyWindowSizeScenarios<IdempotencyWindowSizeClusterFixture>
{
    public IdempotencyWindowSizeTests(IdempotencyWindowSizeClusterFixture fixture) : base(fixture)
    {
    }
}
