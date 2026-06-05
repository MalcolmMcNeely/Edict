using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Schedules;

[Collection(PostgresPersistenceScheduleCollection.Name)]
public sealed class ScheduleSurvivesReactivationTests(PostgresPersistenceScheduleFixture fixture)
    : ScheduleSurvivesReactivationScenarios<PostgresPersistenceScheduleFixture>(fixture);
