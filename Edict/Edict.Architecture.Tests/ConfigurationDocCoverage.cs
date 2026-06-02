using System.Reflection;

namespace Edict.Architecture.Tests;

// Pure doc-coverage matcher: which public-getter properties of an options class
// are absent from its configuration-doc text. The loose substring match is a
// deliberate trade-off — it cannot tell a table cell from prose, so it won't
// catch a knob that drifted out of the table but lingers in prose, but it does
// catch the real failure mode (a knob added to code and forgotten in the docs
// entirely) without firing on cosmetic table reformatting.
static class ConfigurationDocCoverage
{
    public static IReadOnlyCollection<string> FindUndocumentedProperties(Type optionsType, string docText)
    {
        return optionsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .Where(name => !docText.Contains(name, StringComparison.Ordinal))
            .ToList();
    }
}
