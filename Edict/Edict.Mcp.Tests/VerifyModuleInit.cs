using System.Runtime.CompilerServices;

using Microsoft.Build.Locator;

using VerifyTests;

namespace Edict.Mcp.Tests;

static class VerifyModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
            new PathInfo(Path.Combine(projectDirectory, "Snapshots"), type.Name, method.Name));

        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
}
