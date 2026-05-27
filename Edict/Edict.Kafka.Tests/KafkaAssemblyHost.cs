using Testcontainers.Kafka;

namespace Edict.Kafka.Tests;

/// <summary>
/// Assembly-scoped Kafka testcontainer (confluentinc/cp-kafka via
/// Testcontainers.Kafka). Each fixture mints its own consumer group so the
/// shared container can host multiple parallel fixture runs without
/// cross-fixture offset contamination.
/// </summary>
static class KafkaAssemblyHost
{
    static readonly Lazy<Task<KafkaContainer>> _container =
        new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    static KafkaAssemblyHost()
    {
        AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
        {
            if (_container.IsValueCreated)
            {
                try
                {
                    var container = await _container.Value;
                    await container.DisposeAsync();
                }
                catch
                {
                    // Best-effort teardown.
                }
            }
        };
    }

    public static async Task<string> GetBootstrapServersAsync()
    {
        var container = await _container.Value;
        var address = container.GetBootstrapAddress();
        return address.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? address.Substring("PLAINTEXT://".Length)
            : address;
    }

    static async Task<KafkaContainer> StartAsync()
    {
        var container = new KafkaBuilder().Build();
        await container.StartAsync();
        return container;
    }
}
