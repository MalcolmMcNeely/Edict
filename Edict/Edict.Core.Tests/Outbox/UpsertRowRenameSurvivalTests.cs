using Edict.Contracts.Persistence;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.Serialization.TypeSystem;

namespace Edict.Core.Tests.Outbox;

// ADR 0027 rename-survival proof — closes the AQTN hole resiliency-analysis.md
// §3.2.3 flagged on UpsertRowEffect.RowTypeName. The previous shape used the
// row POCO's AssemblyQualifiedName as identity, so a consumer who renamed the
// class dead-lettered every in-flight entry. The new shape captures the frozen
// [Alias] literal via Orleans's TypeConverter and resolves it back on drain.
// The class-name-differs-from-alias scenario here is the "after rename" state:
// proving that even when the C# identifier and the alias literal diverge, the
// publish→drain round-trip remains stable.

// A row POCO whose class name (RenamedRowAfterAdr0027) deliberately differs
// from its frozen [Alias] literal ("OriginalRowNameBeforeAdr0027"). This is
// exactly the state a consumer ends up in after they rename the class but
// preserve the alias — the rename the previous AQTN-based mechanism would have
// dead-lettered every in-flight entry through.
[GenerateSerializer]
[Alias("OriginalRowNameBeforeAdr0027")]
public sealed class RenamedRowAfterAdr0027 : IEdictPersistedState
{
    [Id(0)]
    public int Marker { get; set; }
}

[Collection(EdictClusterCollection.Name)]
public sealed class UpsertRowRenameSurvivalTests(EdictClusterFixture fixture)
{
    [Fact]
    public void Format_ShouldCaptureFrozenAliasLiteral_NotSimpleClassName()
    {
        // The TypeConverter Format hop is what the publisher
        // (EdictTableProjectionBuilder.BuildUpsertEntry) calls; its output is
        // what RowAlias on the UpsertRowEffect carries. The literal "...Before...",
        // not the C# identifier "...After...", is what travels — the rename has
        // not broken the wire identity.
        var converter = fixture.Cluster.ServiceProvider.GetRequiredService<TypeConverter>();

        var formatted = converter.Format(typeof(RenamedRowAfterAdr0027));

        Assert.Equal("OriginalRowNameBeforeAdr0027", formatted);
    }

    [Fact]
    public void Parse_ShouldResolveFrozenAliasBackToCurrentClass_AfterClassRename()
    {
        // The drain (UpsertRowExecutor) calls TypeConverter.Parse with the
        // captured alias. Even though the C# class has been renamed, the
        // resolved Type is the current class — so an in-flight entry written
        // before the rename still drains successfully after the rename.
        var converter = fixture.Cluster.ServiceProvider.GetRequiredService<TypeConverter>();

        var resolved = converter.Parse("OriginalRowNameBeforeAdr0027");

        Assert.Equal(typeof(RenamedRowAfterAdr0027), resolved);
    }

    [Fact]
    public void FormatAndParse_ShouldRoundTrip_ForPostRenamePoco()
    {
        // The end-to-end claim that closes the AQTN hole: publisher's Format
        // gives a literal the consumer chose; drain's Parse returns the same
        // Type even when the class identifier has changed since. A class
        // rename now requires only that the [Alias] literal stay intact —
        // exactly the discipline ADR 0017 already mandated and EDICT011 enforces.
        var converter = fixture.Cluster.ServiceProvider.GetRequiredService<TypeConverter>();

        var formatted = converter.Format(typeof(RenamedRowAfterAdr0027));
        var roundTripped = converter.Parse(formatted);

        Assert.Equal(typeof(RenamedRowAfterAdr0027), roundTripped);
    }
}
