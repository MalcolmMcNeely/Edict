using Edict.Tests.Conformance.Idempotency;

namespace Edict.Postgres.Tests.Idempotency;

[Collection(IdempotencyWindowSizePostgresCollection.Name)]
public sealed class IdempotencyWindowSizePostgresTests(IdempotencyWindowSizePostgresFixture fixture)
    : IdempotencyWindowSizeScenarios<IdempotencyWindowSizePostgresFixture>(fixture);
