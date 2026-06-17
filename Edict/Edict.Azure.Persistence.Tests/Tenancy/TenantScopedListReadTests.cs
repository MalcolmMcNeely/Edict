using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Tenancy;

[Collection(AzureTenancyCollection.Name)]
public sealed class TenantScopedListReadTests(AzureTenancyFixture fixture)
    : TenantScopedListReadScenarios<AzureTenancyFixture>(fixture);
