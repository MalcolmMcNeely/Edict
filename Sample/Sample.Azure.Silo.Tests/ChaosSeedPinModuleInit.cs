using System.Runtime.CompilerServices;

namespace Sample.Azure.Silo.Tests;

// Temporary: pins the consumer dogfood suite to the historical chaos seed so its
// characterization snapshots stay stable now that the run-wide default is random.
// A developer can still override by exporting EDICT_CHAOS_SEED before the run. The
// follow-up slice that adds WithoutChaos() migrates each timeline-Verify test off
// the pin (and keeps the invariant tests on the varying seed), at which point this
// file is deleted.
static class ChaosSeedPinModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        if (Environment.GetEnvironmentVariable("EDICT_CHAOS_SEED") is null)
        {
            Environment.SetEnvironmentVariable("EDICT_CHAOS_SEED", "0xED1C7");
        }
    }
}
