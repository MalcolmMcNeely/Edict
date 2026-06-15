using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Audit;

[Collection(AzureAuditCollection.Name)]
public sealed class AzureAuditCorrelationQueryTests(AzureAuditFixture fixture)
    : AuditCorrelationQueryScenarios<AzureAuditFixture>(fixture);
