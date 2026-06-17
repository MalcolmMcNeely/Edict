using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

[Collection(PostgresTenancyDeadLetterCollection.Name)]
public sealed class TenantTaggedDeadLetterTests(PostgresTenancyDeadLetterFixture fixture)
    : TenantTaggedDeadLetterScenarios<PostgresTenancyDeadLetterFixture>(fixture);
