using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Tenancy;

[Collection(AzureTenancyDeadLetterCollection.Name)]
public sealed class TenantTaggedDeadLetterTests(AzureTenancyDeadLetterFixture fixture)
    : TenantTaggedDeadLetterScenarios<AzureTenancyDeadLetterFixture>(fixture);
