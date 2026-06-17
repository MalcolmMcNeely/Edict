using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Outbox;

[Collection(AzurePersistenceOutboxFaultGranularityCollection.Name)]
public sealed class SendCommandFaultThenRecoverTests(AzurePersistenceOutboxFaultGranularityFixture fixture)
    : SendCommandFaultThenRecoverScenarios<AzurePersistenceOutboxFaultGranularityFixture>(fixture);
