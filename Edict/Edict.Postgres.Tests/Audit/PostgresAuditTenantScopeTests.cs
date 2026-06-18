using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Audit;

[Collection(PostgresAuditCollection.Name)]
public sealed class PostgresAuditTenantScopeTests(PostgresAuditFixture fixture)
    : AuditTenantScopeScenarios<PostgresAuditFixture>(fixture);
