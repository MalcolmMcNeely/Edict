using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Tenancy;

[Collection(AzureTenancyCollection.Name)]
public sealed class TenantScopedGrainStateIsolationTests(AzureTenancyFixture fixture)
    : TenantScopedGrainStateIsolationScenarios<AzureTenancyFixture>(fixture);
