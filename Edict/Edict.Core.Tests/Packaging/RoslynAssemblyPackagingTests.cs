using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Xunit;

namespace Edict.Core.Tests.Packaging;

/// <summary>
/// Guards the shipping path of the Edict Roslyn assemblies, not their behaviour.
///
/// The analyzer and generator unit tests (in Edict.Analyzers.Tests /
/// Edict.Generators.Tests) instantiate the analyzer/generator directly and run
/// it over source — they prove the rules are correct, but they say nothing about
/// whether a consumer who does a single <c>PackageReference Include="Edict.Core"</c>
/// actually receives them. Edict.Analyzers is <c>IsPackable=false</c>; it reaches
/// consumers only because Edict.Core's <c>PackEdictRoslynAssemblies</c> target
/// copies both DLLs into the Core nupkg under <c>analyzers/dotnet/cs/</c>, the
/// well-known folder the compiler scans to auto-load analyzers and generators.
///
/// That target is a hand-rolled set of path strings. If someone edits the bin
/// path, renames an assembly, or drops the target, the analyzers silently stop
/// shipping — every other test stays green while real consumer builds quietly
/// lose their EDICT00x diagnostics and source generation. This test is the only
/// thing that fails in that case: it packs Edict.Core and asserts both Roslyn
/// assemblies land in the analyzer slot of the produced package.
/// </summary>
public class RoslynAssemblyPackagingTests
{
#if DEBUG
    const string Configuration = "Debug";
#else
    const string Configuration = "Release";
#endif

    [Fact]
    public async Task EdictCorePackage_ShipsAnalyzerAndGenerator_InTheAnalyzerSlot()
    {
        var coreProject = LocateEdictCoreProject();
        var packOutput = Path.Combine(Path.GetTempPath(), "edict-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packOutput);

        try
        {
            await PackAsync(coreProject, packOutput);

            // GetExtension excludes the sibling Edict.Core.*.snupkg symbol package.
            var package = Directory.GetFiles(packOutput, "Edict.Core.*.nupkg")
                .Single(file => Path.GetExtension(file).Equals(".nupkg", StringComparison.OrdinalIgnoreCase));
            using var archive = ZipFile.OpenRead(package);
            var entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("analyzers/dotnet/cs/Edict.Analyzers.dll", entries);
            Assert.Contains("analyzers/dotnet/cs/Edict.Generators.dll", entries);
        }
        finally
        {
            Directory.Delete(packOutput, recursive: true);
        }
    }

    static string LocateEdictCoreProject([CallerFilePath] string sourceFilePath = "")
    {
        // sourceFilePath -> Edict/Edict.Core.Tests/Packaging/this-file
        var packagingFolder = Path.GetDirectoryName(sourceFilePath)!;
        var coreProject = Path.GetFullPath(Path.Combine(packagingFolder, "..", "..", "Edict.Core", "Edict.Core.csproj"));
        Assert.True(File.Exists(coreProject), $"Could not locate Edict.Core.csproj at '{coreProject}'.");
        return coreProject;
    }

    static async Task PackAsync(string projectPath, string outputDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(Configuration);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--nologo");
        // This test runs inside a test host that already built Edict.Core and both
        // Roslyn assemblies in the same configuration, so pack finds the outputs
        // up to date and assembles the nupkg without recompiling. Disable the
        // persistent MSBuild build server and node reuse so the nested pack does
        // not contend with the test host's MSBuild and stall.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        using var process = Process.Start(startInfo)!;
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"dotnet pack failed (exit {process.ExitCode}).{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }
}
