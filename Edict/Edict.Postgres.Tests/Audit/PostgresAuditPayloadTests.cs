using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Audit;

[Collection(PostgresAuditCollection.Name)]
public sealed class PostgresAuditPayloadTests(PostgresAuditFixture fixture)
    : AuditPayloadScenarios<PostgresAuditFixture>(fixture);
