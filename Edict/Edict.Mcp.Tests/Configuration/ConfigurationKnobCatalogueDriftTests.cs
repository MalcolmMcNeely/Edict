using System.Reflection;

using Edict.Azure.Persistence;
using Edict.Azure.Streaming;
using Edict.Azure.Streaming.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Kafka;
using Edict.Mcp.Configuration;
using Edict.Postgres;

using Xunit;

namespace Edict.Mcp.Tests.Configuration;

public class ConfigurationKnobCatalogueDriftTests
{
    static readonly Type[] OptionsTypes =
    [
        typeof(EdictOptions),
        typeof(EdictSagaOptions),
        typeof(EdictCommandHandlerScheduleOptions),
        typeof(EdictAzureStreamsOptions),
        typeof(EdictAzurePersistenceOptions),
        typeof(EdictAzureBlobClaimCheckOptions),
        typeof(EdictKafkaStreamsOptions),
        typeof(EdictPostgresPersistenceOptions),
    ];

    [Fact]
    public void Catalogue_CoversEveryPublicGetterPropertyOnEveryOptionsClass()
    {
        var drift = new List<string>();
        foreach (var optionsType in OptionsTypes)
        {
            var entry = ConfigurationKnobCatalogue.Entries.SingleOrDefault(candidate => candidate.OptionsTypeName == optionsType.Name);
            if (entry is null)
            {
                drift.Add($"No catalogue entry for options class '{optionsType.Name}'.");
                continue;
            }
            var cataloguedKnobs = entry.Knobs.Select(knob => knob.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var property in PublicGetterInstanceProperties(optionsType))
            {
                if (!cataloguedKnobs.Contains(property.Name))
                {
                    drift.Add($"{optionsType.Name}.{property.Name} has no catalogue entry.");
                }
            }
        }

        Assert.Empty(drift);
    }

    [Fact]
    public void Catalogue_CoversExactlyTheEightConsumerFacingOptionsClasses()
    {
        var catalogued = ConfigurationKnobCatalogue.Entries.Select(entry => entry.OptionsTypeName).ToHashSet(StringComparer.Ordinal);
        var reflected = OptionsTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(reflected, catalogued);
    }

    static IEnumerable<PropertyInfo> PublicGetterInstanceProperties(Type optionsType) =>
        optionsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetGetMethod() is not null);
}
