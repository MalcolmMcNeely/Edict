using Edict.Contracts.Tenancy;

namespace Sample.Web.Components.State;

/// <summary>
/// Demo-scope shared state for the signed-in company. The B2B pages read
/// <see cref="Current"/> to scope every send and read to one tenant's wall, and the
/// AddEdictTenant edge resolver yields it so a stolen route key into another company is
/// denied. Stubs the signed-tenant seam an identity provider would supply in production:
/// a real app maps the user's tenant claim here, never a value off the request body.
/// Registered as a singleton — fine for a single-user local demo, intentionally not safe
/// for multi-user production.
/// </summary>
public sealed class TenantSwitcher
{
    readonly List<TenantCompany> _companies =
    [
        new(EdictTenantId.Of("acme"), "Acme Corp"),
        new(EdictTenantId.Of("globex"), "Globex"),
    ];

    public TenantSwitcher() => Current = _companies[0].Tenant;

    /// <summary>The companies a visitor can act as, seeded with two and grown by onboarding.</summary>
    public IReadOnlyList<TenantCompany> Companies => _companies;

    /// <summary>The company whose wall every B2B send and read is scoped to right now.</summary>
    public EdictTenantId Current { get; private set; }

    /// <summary>Acts as an existing company.</summary>
    public void Switch(EdictTenantId tenant) => Current = tenant;

    /// <summary>
    /// Mints a brand-new company from a display name and acts as it: the tenant id is a
    /// charset-safe slug the public-to-tenant establishing crossing then stamps. Returns the
    /// new company so the onboarding flow can show the wall it just created.
    /// </summary>
    public TenantCompany Register(string displayName)
    {
        var slug = Slugify(displayName);
        var tenant = EdictTenantId.Of($"{slug}-{Guid.NewGuid().ToString("N")[..8]}");
        var company = new TenantCompany(tenant, displayName.Trim());
        _companies.Add(company);
        Current = tenant;
        return company;
    }

    public string DisplayNameFor(EdictTenantId tenant) =>
        _companies.FirstOrDefault(company => company.Tenant == tenant)?.DisplayName ?? tenant.Value;

    // Reduces a display name to the EdictTenantId safe-everywhere charset (ASCII
    // letters, digits, and . _ -): lowercases, keeps alphanumerics, collapses any
    // other run to a single hyphen. Empty input falls back to a stable stem so Of()
    // always receives a non-empty value.
    static string Slugify(string displayName)
    {
        var characters = displayName.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(characters).Trim('-');
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }
        return slug.Length == 0 ? "company" : slug;
    }
}

/// <summary>A company a visitor can act as: the tenant wall plus its human-readable name.</summary>
public sealed record TenantCompany(EdictTenantId Tenant, string DisplayName);
