using Edict.Postgres.TableStorage;
using Edict.Telemetry;

using Npgsql;

namespace Edict.Postgres.Tenancy;

/// <summary>
/// The Postgres-only second wall behind tenant key composition. Composition is the
/// primary control; this is defense-in-depth at the database layer, so a query that
/// addresses another tenant's rows comes back empty even when the composed key would
/// admit it. Two halves: the DDL that arms a <c>FOR SELECT</c> row-security policy on a
/// tenant-keyed table, and the per-transaction <c>SET LOCAL</c> that establishes the
/// reading tenant. The tenant for <c>SET LOCAL</c> is the per-turn ambient relay, seeded
/// from the authenticated origin — independent of the queried key, which is exactly what
/// turns a keying bug into a database denial rather than a silent leak.
/// <para>
/// The policy is permissive when no tenant is in the session: a single-tenant app, a
/// public aggregate, an operator read, and the silo's own bootstrap all run with no
/// <c>SET LOCAL</c> and see every row, so the wall costs a non-tenant consumer nothing.
/// Postgres enforces RLS only against a connection whose role is neither a superuser nor
/// the table owner under <c>FORCE</c>, so the production connection must use such a role
/// for the backstop to be real — the asymmetry the Azure pairing has no equivalent for.
/// </para>
/// </summary>
internal static class PostgresTenantRowSecurity
{
    // The session GUC the policy reads. A dotted custom-placeholder name so an unset read
    // through current_setting(..., true) returns null rather than erroring.
    const string TenantSetting = "edict.tenant";

    // The single policy name, reused per table — policy names are scoped per table, so a
    // constant keeps the DDL uniform across the grain-state table and every projection
    // table.
    const string PolicyName = "edict_tenant_isolation";

    /// <summary>
    /// DDL that arms the row-security policy on a tenant-keyed table whose
    /// <paramref name="keyColumn"/> carries the tenant as the <c>{tenant}|...</c> prefix
    /// (the whole value for a tenant-as-partition column that holds the bare tenant).
    /// Idempotent and race-safe: <c>ENABLE</c>/<c>FORCE</c> are no-ops once set, and the
    /// policy is created only when <c>pg_policies</c> shows it absent, so concurrent
    /// first-writes to the same projection table do not collide on a duplicate policy.
    /// </summary>
    internal static string PolicySql(string tableName, string keyColumn)
    {
        var quoted = PostgresTableSchema.QuoteIdentifier(tableName);
        var tableLiteral = "'" + tableName.Replace("'", "''") + "'";
        return
            $"ALTER TABLE {quoted} ENABLE ROW LEVEL SECURITY;" +
            $"ALTER TABLE {quoted} FORCE ROW LEVEL SECURITY;" +
            "DO $$ BEGIN " +
            $"IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = {tableLiteral} AND policyname = '{PolicyName}') THEN " +
            $"CREATE POLICY {PolicyName} ON {quoted} FOR SELECT USING (" +
            $"current_setting('{TenantSetting}', true) IS NULL " +
            $"OR current_setting('{TenantSetting}', true) = '' " +
            $"OR split_part({keyColumn}, '|', 1) = current_setting('{TenantSetting}', true)); " +
            "END IF; END $$;";
    }

    /// <summary>
    /// Opens a transaction and binds the ambient tenant to it as a transaction-local GUC,
    /// so the row-security policy engages for the reads that run on the same connection
    /// before the returned transaction is committed. <c>set_config(..., is_local =&gt;
    /// true)</c> is the parameterisable form of <c>SET LOCAL</c>; plain <c>SET</c> takes
    /// no placeholder. Returns <see langword="null"/> when no tenant is in scope, so the
    /// caller reads without a transaction and the permissive policy branch returns every
    /// row.
    /// </summary>
    internal static async Task<NpgsqlTransaction?> BeginTenantScopeAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var ambientTenant = TenantRelay.Current()?.Value;
        if (ambientTenant is null)
        {
            return null;
        }

        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var setLocal = connection.CreateCommand();
        setLocal.Transaction = transaction;
        setLocal.CommandText = $"SELECT set_config('{TenantSetting}', @edict_tenant, true);";
        setLocal.Parameters.AddWithValue("edict_tenant", ambientTenant);
        await setLocal.ExecuteNonQueryAsync(cancellationToken);
        return transaction;
    }
}
