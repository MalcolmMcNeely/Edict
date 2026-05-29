using System.Reflection;

namespace Edict.Benchmarks.Throughput.Measurement;

/// <summary>
/// Probe for the interceptor build-time toggle. The unified Edict generator
/// emits per-type <c>file static class SendInterceptor_*</c> stubs into the
/// <c>Edict.Generated</c> namespace when <c>EdictInterceptorsEnabled</c>
/// is true (default). Reflecting over the current assembly is the cheapest
/// indirect proof that the toggle landed in the compiled output.
/// </summary>
public static class InterceptorProbe
{
    public static bool SendInterceptorsEmitted()
    {
        var assembly = typeof(InterceptorProbe).Assembly;
        return assembly.GetTypes().Any(t =>
            t.IsClass
            && t.Name.StartsWith("SendInterceptor_", StringComparison.Ordinal));
    }
}
