using Edict.Mcp.Versioning;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Versioning;

public class EdictDriftStderrFormatterTests
{
    [Fact]
    public Task Format_CleanReport_ReturnsNull()
    {
        // Arrange
        var report = new EdictVersionReport(
            ToolVersion: "0.1.0-preview.42",
            References:
            [
                new EdictVersionReference("Edict.Core", "0.1.0-preview.42", ["ConsumerProject"]),
            ],
            IsDrifted: false,
            HasNoEdictReferences: false,
            HasInconsistentLibraryVersions: false);

        // Act
        var message = EdictDriftStderrFormatter.Format(report);

        // Assert
        Assert.Null(message);
        return Task.CompletedTask;
    }

    [Fact]
    public Task Format_DriftedReport_NamesToolVersion_LibraryVersions_AndRemediation()
    {
        // Arrange
        var report = new EdictVersionReport(
            ToolVersion: "0.1.0-preview.42",
            References:
            [
                new EdictVersionReference("Edict.Core", "0.1.0-preview.41", ["ConsumerProject"]),
            ],
            IsDrifted: true,
            HasNoEdictReferences: false,
            HasInconsistentLibraryVersions: false);

        // Act
        var message = EdictDriftStderrFormatter.Format(report);

        // Assert
        return Verify(message);
    }

    [Fact]
    public Task Format_NoEdictReferencesReport_NamesEmptyWorkspaceRemediation()
    {
        // Arrange
        var report = new EdictVersionReport(
            ToolVersion: "0.1.0-preview.42",
            References: Array.Empty<EdictVersionReference>(),
            IsDrifted: false,
            HasNoEdictReferences: true,
            HasInconsistentLibraryVersions: false);

        // Act
        var message = EdictDriftStderrFormatter.Format(report);

        // Assert
        return Verify(message);
    }

    [Fact]
    public Task Format_InconsistentLibraryVersionsReport_ListsDistinctVersions_AndLockstepRemediation()
    {
        // Arrange
        var report = new EdictVersionReport(
            ToolVersion: "0.1.0-preview.42",
            References:
            [
                new EdictVersionReference("Edict.Core", "0.1.0-preview.41", ["ConsumerProjectA"]),
                new EdictVersionReference("Edict.Core", "0.1.0-preview.42", ["ConsumerProjectB"]),
            ],
            IsDrifted: true,
            HasNoEdictReferences: false,
            HasInconsistentLibraryVersions: true);

        // Act
        var message = EdictDriftStderrFormatter.Format(report);

        // Assert
        return Verify(message);
    }
}
