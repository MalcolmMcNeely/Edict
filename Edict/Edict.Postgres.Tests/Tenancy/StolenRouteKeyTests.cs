using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

[Collection(PostgresStolenRouteKeyCollection.Name)]
public sealed class StolenRouteKeyTests(PostgresStolenRouteKeyFixture fixture)
    : StolenRouteKeyScenarios<PostgresStolenRouteKeyFixture>(fixture);
