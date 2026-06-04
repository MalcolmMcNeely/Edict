using Edict.Contracts.Persistence;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization.TypeSystem;

namespace Edict.Azure.Persistence.Tests.Recovery;

// A row POCO whose class name deliberately differs from its frozen [Alias]
// literal — the state a consumer is in after they rename the class but
// preserve the alias.
[GenerateSerializer]
[Alias("OriginalRowNameBeforeAdr0027")]
public sealed class RenamedRowAfterAdr0027 : IEdictPersistedState
{
    [Id(0)]
    public int Marker { get; set; }
}

[Collection(AzurePersistenceRecoveryCollection.Name)]
public sealed class UpsertRowRenameSurvivalTests(AzurePersistenceRecoveryFixture fixture)
{
    [Fact]
    public void Format_ShouldCaptureFrozenAliasLiteral_NotSimpleClassName()
    {
        var converter = fixture.Cluster.ServiceProvider.GetRequiredService<TypeConverter>();

        var formatted = converter.Format(typeof(RenamedRowAfterAdr0027));

        Assert.Equal("OriginalRowNameBeforeAdr0027", formatted);
    }

    [Fact]
    public void Parse_ShouldResolveFrozenAliasBackToCurrentClass_AfterClassRename()
    {
        var converter = fixture.Cluster.ServiceProvider.GetRequiredService<TypeConverter>();

        var resolved = converter.Parse("OriginalRowNameBeforeAdr0027");

        Assert.Equal(typeof(RenamedRowAfterAdr0027), resolved);
    }

    [Fact]
    public void FormatAndParse_ShouldRoundTrip_ForPostRenamePoco()
    {
        var converter = fixture.Cluster.ServiceProvider.GetRequiredService<TypeConverter>();

        var formatted = converter.Format(typeof(RenamedRowAfterAdr0027));
        var roundTripped = converter.Parse(formatted);

        Assert.Equal(typeof(RenamedRowAfterAdr0027), roundTripped);
    }
}
