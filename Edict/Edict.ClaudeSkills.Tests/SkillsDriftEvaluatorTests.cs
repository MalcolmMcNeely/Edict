using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class SkillsDriftEvaluatorTests
{
    const string SkillName = "edict-authoring";
    const string EmbeddedHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string DiskHashMatchingManifest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string DiskHashDifferingFromManifest = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void ManifestPresent_DiskPresent_HashesMatch_PlainInstall_ReturnsRefresh()
    {
        var decision = Evaluate(
            manifestHashForSkill: DiskHashMatchingManifest,
            diskHashForSkill: DiskHashMatchingManifest,
            force: false);
        Assert.Equal(SkillDriftDecision.Refresh, decision);
    }

    [Fact]
    public void ManifestPresent_DiskPresent_HashesMatch_Force_ReturnsRefresh()
    {
        var decision = Evaluate(
            manifestHashForSkill: DiskHashMatchingManifest,
            diskHashForSkill: DiskHashMatchingManifest,
            force: true);
        Assert.Equal(SkillDriftDecision.Refresh, decision);
    }

    [Fact]
    public void ManifestPresent_DiskPresent_HashesDiffer_PlainInstall_ReturnsSkipDrifted()
    {
        var decision = Evaluate(
            manifestHashForSkill: DiskHashMatchingManifest,
            diskHashForSkill: DiskHashDifferingFromManifest,
            force: false);
        Assert.Equal(SkillDriftDecision.SkipDrifted, decision);
    }

    [Fact]
    public void ManifestPresent_DiskPresent_HashesDiffer_Force_ReturnsRefresh()
    {
        var decision = Evaluate(
            manifestHashForSkill: DiskHashMatchingManifest,
            diskHashForSkill: DiskHashDifferingFromManifest,
            force: true);
        Assert.Equal(SkillDriftDecision.Refresh, decision);
    }

    [Fact]
    public void ManifestPresent_DiskAbsent_PlainInstall_ReturnsCreate()
    {
        var decision = Evaluate(
            manifestHashForSkill: DiskHashMatchingManifest,
            diskHashForSkill: null,
            force: false);
        Assert.Equal(SkillDriftDecision.Create, decision);
    }

    [Fact]
    public void ManifestPresent_DiskAbsent_Force_ReturnsCreate()
    {
        var decision = Evaluate(
            manifestHashForSkill: DiskHashMatchingManifest,
            diskHashForSkill: null,
            force: true);
        Assert.Equal(SkillDriftDecision.Create, decision);
    }

    [Fact]
    public void ManifestAbsent_DiskPresent_PlainInstall_ReturnsSkipDrifted()
    {
        var decision = Evaluate(
            manifestHashForSkill: null,
            diskHashForSkill: DiskHashDifferingFromManifest,
            force: false);
        Assert.Equal(SkillDriftDecision.SkipDrifted, decision);
    }

    [Fact]
    public void ManifestAbsent_DiskPresent_Force_ReturnsRefresh()
    {
        var decision = Evaluate(
            manifestHashForSkill: null,
            diskHashForSkill: DiskHashDifferingFromManifest,
            force: true);
        Assert.Equal(SkillDriftDecision.Refresh, decision);
    }

    [Fact]
    public void ManifestAbsent_DiskAbsent_PlainInstall_ReturnsCreate()
    {
        var decision = Evaluate(
            manifestHashForSkill: null,
            diskHashForSkill: null,
            force: false);
        Assert.Equal(SkillDriftDecision.Create, decision);
    }

    [Fact]
    public void ManifestAbsent_DiskAbsent_Force_ReturnsCreate()
    {
        var decision = Evaluate(
            manifestHashForSkill: null,
            diskHashForSkill: null,
            force: true);
        Assert.Equal(SkillDriftDecision.Create, decision);
    }

    [Fact]
    public void EntirelyAbsentManifest_NoMatterDiskState_TreatsEveryFileAsBootstrapCase()
    {
        // Arrange — three skills: one on disk, two not. Manifest entirely absent (upgrade bootstrap).
        var embedded = new[]
        {
            new EmbeddedSkillDescriptor("edict-authoring", EmbeddedHash),
            new EmbeddedSkillDescriptor("edict-contracts", EmbeddedHash),
            new EmbeddedSkillDescriptor("edict-testing", EmbeddedHash),
        };
        var onDisk = new[]
        {
            new OnDiskSkillDescriptor("edict-authoring", DiskHashDifferingFromManifest),
        };

        // Act
        var plain = SkillsDriftEvaluator.Evaluate(currentManifest: null, embedded, onDisk, force: false);
        var forced = SkillsDriftEvaluator.Evaluate(currentManifest: null, embedded, onDisk, force: true);

        // Assert
        Assert.Equal(SkillDriftDecision.SkipDrifted, plain["edict-authoring"]);
        Assert.Equal(SkillDriftDecision.Create, plain["edict-contracts"]);
        Assert.Equal(SkillDriftDecision.Create, plain["edict-testing"]);

        Assert.Equal(SkillDriftDecision.Refresh, forced["edict-authoring"]);
        Assert.Equal(SkillDriftDecision.Create, forced["edict-contracts"]);
        Assert.Equal(SkillDriftDecision.Create, forced["edict-testing"]);
    }

    static SkillDriftDecision Evaluate(string? manifestHashForSkill, string? diskHashForSkill, bool force)
    {
        var embedded = new[] { new EmbeddedSkillDescriptor(SkillName, EmbeddedHash) };
        var onDisk = diskHashForSkill is null
            ? Array.Empty<OnDiskSkillDescriptor>()
            : [new OnDiskSkillDescriptor(SkillName, diskHashForSkill)];
        SkillsManifest? manifest = manifestHashForSkill is null
            ? null
            : new SkillsManifest(
                InstalledVersion: "0.2.0",
                Skills: new Dictionary<string, string>(StringComparer.Ordinal) { [SkillName] = manifestHashForSkill });

        var decisions = SkillsDriftEvaluator.Evaluate(manifest, embedded, onDisk, force);
        return decisions[SkillName];
    }
}
