using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Tenancy;

[Collection(AzureStolenRouteKeyCollection.Name)]
public sealed class StolenRouteKeyTests(AzureStolenRouteKeyFixture fixture)
    : StolenRouteKeyScenarios<AzureStolenRouteKeyFixture>(fixture);
