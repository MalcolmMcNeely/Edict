# Multi-tenancy

Edict's multi-tenancy gives you one guarantee, drawn as a hard line, so you always know which side of it a given concern lives on:

- **The Company Wall (Edict's job).** A session resolved as Company A can only ever address Company A's data. Its commands route to Company A's grains, its events publish to Company A's streams, its reads see only Company A's projection rows, and its audit trail shows only Company A's records. This is **structural**: the tenant is folded into every composed grain and stream key, so reaching another tenant's data is not a permission that was forgotten, it is a key that cannot be formed.
- **Within-tenant authorization (your job).** *Which user inside Company A* may see or change a given row is your application's authorization layer, and Edict never touches it. The map from a logged-in user to their tenant is also yours: you resolve it from a trusted, signed source and hand Edict the tenant id.

Keep those two apart and the rest follows. Edict closes the cross-tenant hole (the one that leaks a whole company's data) at the framework level; you keep owning the within-company decisions that depend on your domain.

## What you author: `[EdictTenantScoped]` on the route-key type

Tenancy is per-aggregate and opt-in. You mark an aggregate tenant-scoped by putting `[EdictTenantScoped]` (from `Edict.Contracts.Tenancy`) on its **route-key type**, which is therefore a small wrapper `struct` rather than a bare `Guid`:

```csharp
using Edict.Contracts.Commands;
using Edict.Contracts.Tenancy;

[EdictTenantScoped]
public readonly record struct EmployeeId(Guid Value);

public sealed partial record AddEmployeeCommand(
    [property: EdictRouteKey] EmployeeId EmployeeId,
    string FullName) : EdictCommand;
```

Every Command and Event keyed by that type now lives behind the wall, and its grain and stream keys compose as `{tenant}|{guid}` instead of the bare `{guid}` of a public aggregate.

The marker sits on the route-key type rather than on each message on purpose. An aggregate is a cluster of messages sharing one route key; declaring tenancy once on the type makes a leak-by-drift across that cluster unrepresentable, where a per-message attribute could be applied to four of five messages and leak on the fifth.

A **public aggregate** (orders in a B2C store, say) keeps an unmarked route-key type and is never walled. Public and tenant-scoped aggregates coexist freely in one app: a single deployment can serve anonymous public orders and walled per-company employee directories side by side.

## What you wire: `AddEdictTenant`

Registering the tenant edge resolver is the whole multi-tenancy opt-in. Wire it on every silo and on any client or Web front end that issues commands or reads tenant-scoped projections:

```csharp
silo.Services.AddEdictTenant(serviceProvider =>
    serviceProvider.GetRequiredService<IHttpContextAccessor>()
        .ResolveTenantFromSignedClaim());
```

The resolver returns the ambient `EdictTenantId?` for the current call, read from a trusted, signed source (a session claim, a verified header). **Never read the tenant off the request body**: that is a confused-deputy hole, precisely the thing the wall exists to close. The resolver returns `null` when no tenant is in scope.

`EdictTenantId.Of(value)` validates the value against a safe-everywhere character set (ASCII letters, digits, and `.` `_` `-`) and rejects anything else, because the tenant id is folded into a composed key: a value smuggling the key delimiter could forge another tenant's key space, so the character set is a security boundary, not a convenience.

Registering `AddEdictTenant` does two things: it turns on origin tenant-stamping (the resolved tenant is stamped onto the originating command and inherited by every message that send sets in motion), and it installs the runtime isolation backstop, an incoming grain-call filter that refuses any call landing on another tenant's key. A single-tenant app registers nothing and pays no tenant tax; the filter is silent for public aggregates even when present.

## The establishing crossing

Every tenant-scoped send after onboarding is a bare `SendAsync(command)` whose tenant the resolver supplies. The one exception is the public-to-tenant crossing: a "register your company" flow has no ambient tenant yet, because the company does not exist until this send. That single crossing names the tenant explicitly through the establishing-crossing overload:

```csharp
// "Register Acme" — the one public-to-tenant crossing, named explicitly and auditable.
await sender.SendAsync(
    new RegisterCompanyCommand(adminId, "Acme"),
    EdictTenantId.Of(verifiedCompanyId));
```

The same overload is the escape hatch for any context-free origin (a background worker, an import, an admin script) where no edge resolver can supply the tenant. Every crossing of the wall is therefore explicit and visible in the audit log.

## Reading your own tenant's data

A tenant-scoped List projection is read through `IEdictTenantScopedListProjectionReader<TRow>`. Unlike the public `IEdictProjectionReader<TRow>` it takes **no** partition key: the framework composes the caller's ambient tenant into the partition, so "list my employees" passes nothing, and the identical call under a different tenant returns empty *by construction*, not by a permission check.

```csharp
EdictProjectionPartitionRead<EmployeeDirectoryRow> mine =
    await directoryReader.QueryMyPartitionAsync();
```

Read-your-writes (`after:` a cursor) and the read status behave exactly as on the public reader. A tenant-scoped read with no ambient tenant fails closed rather than reading a default partition.

## Fail-closed, always at the edge

The wall fails closed at every point where a missing or wrong tenant could leak data, and always before anything is dispatched, persisted, or read:

- **`EdictMissingTenantException`** is thrown synchronously at an originating `SendAsync` (or a tenant-scoped read) when tenancy is on but the resolver yields no tenant. A tenant-scoped command routed without a tenant would fall into the default key space, a silent cross-tenant leak, so the send is refused at the edge. Supply the tenant explicitly with the establishing-crossing overload, or fix the resolver.
- **`EdictCrossTenantAccessException`** is thrown by the isolation call filter when a call lands on a grain whose key names a tenant other than the calling turn's ambient tenant. On the common path this never fires, because every key is composed from the ambient tenant. It surfaces only on a real divergence: a coding bug that formed a key outside the `EdictKeyComposer` chokepoint, or an illegitimate stolen-key reach into another wall. A stolen-key reach is a direct grain call from the client, so the exception is serialized back across that hop and reaches the caller as itself, with its message intact, rather than as an opaque serialization failure.

Both are enforcement firing correctly, not framework faults. To catch the missing-tenant send case at compile time rather than at runtime, enable the opt-in `EDICT024` analyzer (`dotnet_diagnostic.EDICT024.severity = error`): it flags every bare `IEdictSender.SendAsync(command)` of a tenant-scoped command, and exempts the establishing-crossing overload.

## Two enforcement layers, and the Azure asymmetry

Isolation is enforced in two places. The first is **composition**: the tenant is folded into every key at the `EdictKeyComposer` chokepoint, so a correctly-routed call simply cannot name another tenant's data. The second is the **runtime call filter** described above, which backstops a key formed outside that chokepoint.

On Postgres persistence there is also a third, defense-in-depth layer: Row-Level Security on the grain-state and projection tables, so even a raw query outside Edict cannot cross a tenant boundary. **Azure Table and Blob storage have no equivalent**, so the Azure pairing relies on the composition and call-filter layers alone. This asymmetry is deliberate and stated plainly: if your threat model requires storage-engine-enforced isolation on top of Edict's, choose the Postgres persistence provider.

## What Edict does not do

Drawing the line once more, from the other side:

- **Within-tenant authorization.** Edict does not decide which user inside a tenant may see or change which row. That is your domain authorization layer.
- **The principal-to-tenant map.** Edict does not derive a tenant from a logged-in user. Your resolver supplies the tenant from a trusted source.
- **Cross-tenant operator reads.** The tenant-scoped read surfaces are scoped to one wall by construction. An operator console that legitimately spans all tenants reads through the unscoped repository surfaces instead, which self-describe each row's tenant.

## See also

- [Audit log](audit-log.md) — the audit record carries a first-class tenant, and a tenant-scoped audit read sees only its own wall.
- [Projections](projections.md) — the List projection species behind the ambient-scoped read.
- ADR-0065 (tenant as a routed identity axis) and ADR-0067 (tenant isolation enforcement and storage) for the decisions and their rationale.
