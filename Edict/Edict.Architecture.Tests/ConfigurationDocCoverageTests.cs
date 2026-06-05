using Edict.Azure.Persistence;
using Edict.Azure.Streaming;
using Edict.Azure.Streaming.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Kafka;
using Edict.Postgres;

using Xunit;

namespace Edict.Architecture.Tests;

// Drift guard: every public-getter property on each of the seven options classes
// must appear somewhere in its mapped docs/configuration page. Removing a knob's
// row turns this red, naming the class and the missing property. EdictSagaOptions
// is in the mapping from the first commit so the test is not born red against the
// knob #259 already shipped.
public class ConfigurationDocCoverageTests
{
    public static IEnumerable<object[]> OptionsClassToDocFile() =>
    [
        [typeof(EdictOptions), "core.md"],
        [typeof(EdictSagaOptions), "core.md"],
        [typeof(EdictCommandHandlerScheduleOptions), "core.md"],
        [typeof(EdictAzureStreamsOptions), "azure-streaming.md"],
        [typeof(EdictAzureBlobClaimCheckOptions), "azure-streaming.md"],
        [typeof(EdictAzurePersistenceOptions), "azure-persistence.md"],
        [typeof(EdictKafkaStreamsOptions), "kafka.md"],
        [typeof(EdictPostgresPersistenceOptions), "postgres.md"],
    ];

    [Theory]
    [MemberData(nameof(OptionsClassToDocFile))]
    public void EveryPublicGetterProperty_IsDocumentedInItsConfigurationPage(Type optionsType, string docFileName)
    {
        var docPath = Path.Combine(RepositoryRoot, "docs", "configuration", docFileName);
        Assert.True(File.Exists(docPath), $"Configuration doc missing: {docPath}");

        var docText = File.ReadAllText(docPath);
        var undocumented = ConfigurationDocCoverage.FindUndocumentedProperties(optionsType, docText);

        Assert.True(
            undocumented.Count == 0,
            $"{optionsType.Name} → docs/configuration/{docFileName} is missing a row for: {string.Join(", ", undocumented)}");
    }

    static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !directory.EnumerateFiles("*.slnx").Any())
            {
                directory = directory.Parent;
            }
            return directory?.Parent?.FullName ?? AppContext.BaseDirectory;
        }
    }
}
