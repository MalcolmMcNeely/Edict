using System.Reflection;

namespace Edict.ClaudeSkills;

public sealed class InstallModeDetector
{
    readonly Func<string?> assemblyPathProvider;

    public InstallModeDetector()
        : this(() => Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location)
    {
    }

    public InstallModeDetector(Func<string?> assemblyPathProvider)
    {
        this.assemblyPathProvider = assemblyPathProvider;
    }

    public InstallMode Detect()
    {
        var path = assemblyPathProvider();
        if (string.IsNullOrEmpty(path))
        {
            return InstallMode.Manifest;
        }
        if (path.Contains(@".dotnet\tools\.store", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".dotnet/tools/.store", StringComparison.OrdinalIgnoreCase))
        {
            return InstallMode.Global;
        }
        return InstallMode.Manifest;
    }
}
