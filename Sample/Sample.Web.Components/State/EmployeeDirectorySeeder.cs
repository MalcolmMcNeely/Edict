using Edict.Contracts.Tenancy;

namespace Sample.Web.Components.State;

/// <summary>
/// Demo-scope helper that gives each seeded company a populated directory the first time
/// it is viewed, so switching the tenant swaps one non-empty employee list for a different
/// one rather than two empty tables. Hands out a per-company roster and claims each tenant
/// exactly once, so the Employees page seeds through the establishing crossing only on the
/// first visit. Registered as a singleton — single-user local demo only.
/// </summary>
public sealed class EmployeeDirectorySeeder
{
    static readonly (string Name, string Department)[] AcmeRoster =
    [
        ("Alice Adams", "Engineering"),
        ("Brian Brooks", "Engineering"),
        ("Chloe Carter", "Sales"),
        ("David Diaz", "Finance"),
        ("Erin Evans", "Support"),
    ];

    static readonly (string Name, string Department)[] GlobexRoster =
    [
        ("Frank Foster", "Operations"),
        ("Grace Gibson", "Legal"),
        ("Henry Hughes", "Operations"),
        ("Isla Ingram", "Marketing"),
        ("Jack Jenkins", "Finance"),
    ];

    readonly HashSet<string> _seeded = new();
    readonly object _gate = new();

    /// <summary>The starting roster for a company: Globex gets its own faces, every other tenant Acme's.</summary>
    public IReadOnlyList<(string Name, string Department)> RosterFor(EdictTenantId tenant) =>
        tenant.Value == "globex" ? GlobexRoster : AcmeRoster;

    /// <summary>
    /// Claims <paramref name="tenant"/> for seeding, returning true exactly once per tenant so a
    /// page reload or a poll does not re-seed an already-populated directory.
    /// </summary>
    public bool TryClaimForSeeding(EdictTenantId tenant)
    {
        lock (_gate)
        {
            return _seeded.Add(tenant.Value);
        }
    }
}
