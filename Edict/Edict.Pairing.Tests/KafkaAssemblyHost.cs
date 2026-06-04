using System.Diagnostics;

using Confluent.Kafka;

using Testcontainers.Kafka;

namespace Edict.Pairing.Tests;

/// <summary>
/// Assembly-scoped Kafka testcontainer for the Kafka+Postgres pairing smoke.
/// One broker per xUnit assembly process; each fixture mints its own consumer
/// group so parallel-by-assembly runs do not contaminate offsets. Mirrors the
/// streaming binding's <c>KafkaAssemblyHost</c>, including the host-side metadata
/// round-trip that absorbs the cold-broker settling window Testcontainers' wait
/// strategy misses.
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
        return StripScheme(container.GetBootstrapAddress());
    }

    static async Task<KafkaContainer> StartAsync()
    {
        var container = new KafkaBuilder().Build();
        await container.StartAsync();
        await WaitForKafkaReadyAsync(StripScheme(container.GetBootstrapAddress()));
        return container;
    }

    static async Task WaitForKafkaReadyAsync(string bootstrapServers)
    {
        var deadline = TimeSpan.FromSeconds(60);
        var stopwatch = Stopwatch.StartNew();
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
            SocketTimeoutMs = 5_000,
        };

        Exception? lastError = null;
        while (true)
        {
            try
            {
                using var admin = new AdminClientBuilder(adminConfig).Build();
                var metadata = admin.GetMetadata(TimeSpan.FromSeconds(5));
                if (metadata.Brokers.Count > 0)
                {
                    return;
                }
                lastError = new InvalidOperationException(
                    "AdminClient.GetMetadata succeeded but returned zero brokers.");
            }
            catch (KafkaException exception)
            {
                lastError = exception;
            }

            if (stopwatch.Elapsed >= deadline)
            {
                throw new InvalidOperationException(
                    $"Kafka container reported ready, but the host could not complete an AdminClient.GetMetadata round-trip against '{bootstrapServers}' within {deadline.TotalSeconds:F0} s.",
                    lastError);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    static string StripScheme(string address) =>
        address.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? address.Substring("PLAINTEXT://".Length)
            : address;
}
