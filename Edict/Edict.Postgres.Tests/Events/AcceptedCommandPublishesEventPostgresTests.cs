using Edict.Tests.Conformance.Events;

namespace Edict.Postgres.Tests.Events;

/// <summary>
/// Tracer bullet: bind the substrate-neutral
/// <see cref="AcceptedCommandPublishesEventScenarios{TFixture}"/> battery to
/// the Postgres-persistence × AQS-streams fixture. Proves the seam compiles
/// and runs end-to-end against the new persistence backend; the rest of the
/// conformance battery follows in subsequent commits.
/// </summary>
[Collection(PostgresClusterCollection.Name)]
public sealed class AcceptedCommandPublishesEventPostgresTests
    : AcceptedCommandPublishesEventScenarios<PostgresClusterFixture>
{
    public AcceptedCommandPublishesEventPostgresTests(PostgresClusterFixture fixture)
        : base(fixture)
    {
    }
}
