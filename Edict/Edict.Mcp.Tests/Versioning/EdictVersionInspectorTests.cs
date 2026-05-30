using Edict.Mcp.Versioning;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Versioning;

public class EdictVersionInspectorTests
{
    [Fact]
    public Task Inspect_SingleEdictReferenceAtToolVersion_ReturnsCleanReport()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var edictCoreDllPath = SyntheticEdictAssembly.Emit(
            temporaryDirectory.Path,
            assemblyName: "Edict.Core",
            informationalVersion: "0.1.0-preview.42");
        var solution = SyntheticSolution.WithProjects(
            ("ConsumerProject", new[] { edictCoreDllPath }));
        var inspector = new EdictVersionInspector(toolVersion: "0.1.0-preview.42");

        // Act
        var report = inspector.Inspect(solution);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_SingleEdictReferenceAtDifferentVersion_FlagsDrift()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var edictCoreDllPath = SyntheticEdictAssembly.Emit(
            temporaryDirectory.Path,
            assemblyName: "Edict.Core",
            informationalVersion: "0.1.0-preview.41");
        var solution = SyntheticSolution.WithProjects(
            ("ConsumerProject", new[] { edictCoreDllPath }));
        var inspector = new EdictVersionInspector(toolVersion: "0.1.0-preview.42");

        // Act
        var report = inspector.Inspect(solution);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_NoEdictReferences_FlagsNoReferences()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var unrelatedDllPath = SyntheticEdictAssembly.Emit(
            temporaryDirectory.Path,
            assemblyName: "Contoso.SomeLibrary",
            informationalVersion: "1.2.3");
        var solution = SyntheticSolution.WithProjects(
            ("ConsumerProject", new[] { unrelatedDllPath }));
        var inspector = new EdictVersionInspector(toolVersion: "0.1.0-preview.42");

        // Act
        var report = inspector.Inspect(solution);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_MultipleEdictAssembliesAtSameVersion_ReturnsCleanReport()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var edictCoreDllPath = SyntheticEdictAssembly.Emit(
            temporaryDirectory.Path,
            assemblyName: "Edict.Core",
            informationalVersion: "0.1.0-preview.42");
        var edictContractsDllPath = SyntheticEdictAssembly.Emit(
            temporaryDirectory.Path,
            assemblyName: "Edict.Contracts",
            informationalVersion: "0.1.0-preview.42");
        var solution = SyntheticSolution.WithProjects(
            ("ConsumerProjectA", new[] { edictCoreDllPath }),
            ("ConsumerProjectB", new[] { edictContractsDllPath }));
        var inspector = new EdictVersionInspector(toolVersion: "0.1.0-preview.42");

        // Act
        var report = inspector.Inspect(solution);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_DistinctEdictVersionsAcrossProjects_FlagsInconsistentVersions()
    {
        // Arrange
        using var temporaryDirectoryNew = new TempWorkspaceDirectory();
        using var temporaryDirectoryOld = new TempWorkspaceDirectory();
        var edictCoreNewDllPath = SyntheticEdictAssembly.Emit(
            temporaryDirectoryNew.Path,
            assemblyName: "Edict.Core",
            informationalVersion: "0.1.0-preview.42");
        var edictCoreOldDllPath = SyntheticEdictAssembly.Emit(
            temporaryDirectoryOld.Path,
            assemblyName: "Edict.Core",
            informationalVersion: "0.1.0-preview.41");
        var solution = SyntheticSolution.WithProjects(
            ("ConsumerProjectA", new[] { edictCoreNewDllPath }),
            ("ConsumerProjectB", new[] { edictCoreOldDllPath }));
        var inspector = new EdictVersionInspector(toolVersion: "0.1.0-preview.42");

        // Act
        var report = inspector.Inspect(solution);

        // Assert
        return Verify(report);
    }
}
