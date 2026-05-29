using System.Runtime.CompilerServices;

using VerifyTests;

namespace Edict.Benchmarks.Throughput.Tests;

static class VerifyModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
            new PathInfo(Path.Combine(projectDirectory, "Snapshots"), type.Name, method.Name));
        // Verify replaces dates with counter placeholders by default; show the
        // literal value so snapshots assert byte-for-byte round-trip fidelity.
        VerifierSettings.DontScrubDateTimes();
    }
}
