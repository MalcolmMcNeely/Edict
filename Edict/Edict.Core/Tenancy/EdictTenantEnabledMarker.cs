namespace Edict.Core.Tenancy;

// Presence in the container is the tenant-stamping on-switch. Registered by
// AddEdictTenant alongside the resolver, so a single-tenant app that registers
// nothing leaves the marker absent and the stamper inert.
sealed class EdictTenantEnabledMarker;
