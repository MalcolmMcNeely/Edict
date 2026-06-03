using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Outbox;

[Collection(AzurePersistenceControllableExecutorCollection.Name)]
public sealed class OutboxRecoveryAfterCrashTests(AzurePersistenceControllableExecutorFixture fixture)
    : OutboxRecoveryAfterCrashScenarios<AzurePersistenceControllableExecutorFixture>(fixture);
