using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Schedules;

[Collection(AzurePersistenceScheduleCollection.Name)]
public sealed class ScheduleSurvivesReactivationTests(AzurePersistenceScheduleFixture fixture)
    : ScheduleSurvivesReactivationScenarios<AzurePersistenceScheduleFixture>(fixture);
