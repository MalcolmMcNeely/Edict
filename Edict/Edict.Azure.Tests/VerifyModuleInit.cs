using System.Runtime.CompilerServices;

using VerifyTests;

namespace Edict.Azure.Tests;

static class VerifyModuleInit
{
    [ModuleInitializer]
    public static void Init() =>
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
            new PathInfo(Path.Combine(projectDirectory, "Snapshots"), type.Name, method.Name));
}
