using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

[Collection(PostgresTenancyCollection.Name)]
public sealed class TenantScopedListReadTests(PostgresTenancyFixture fixture)
    : TenantScopedListReadScenarios<PostgresTenancyFixture>(fixture);
