using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Audit;

[Collection(AzureAuditCollection.Name)]
public sealed class AzureAuditPrincipalQueryTests(AzureAuditFixture fixture)
    : AuditPrincipalQueryScenarios<AzureAuditFixture>(fixture);
