namespace Edict.Contracts.Tenancy;

/// <summary>
/// Marks a route-key type as tenant-scoped: every aggregate keyed by it lives
/// behind the company wall, its grain and stream keys composing as
/// <c>{tenant}|{guid}</c> rather than the bare <c>{guid}</c> of a public
/// aggregate. The marker sits on the route-key type, not on each message,
/// because an aggregate is a cluster of messages sharing one route key;
/// declaring tenancy once on the type makes a leak-by-drift across that cluster
/// unrepresentable, where a per-message attribute could be applied to four of
/// five messages and leak on the fifth.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class EdictTenantScopedAttribute : Attribute;
