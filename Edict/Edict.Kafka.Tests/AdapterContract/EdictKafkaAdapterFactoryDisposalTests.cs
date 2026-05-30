using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Kafka.Internal;
using Edict.Tests.Conformance;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;

using Xunit;

namespace Edict.Kafka.Tests.AdapterContract;

/// <summary>
/// Across-lifecycle regression guard for <see cref="EdictKafkaAdapterFactory"/>.
/// When the factory is disposed (e.g. by DI as the silo shuts down) and the
/// <see cref="Lazy{T}"/>-held adapter has been materialised, the underlying
/// <c>IProducer</c> must flush and dispose so its librdkafka background threads
/// and TCP connections don't leak into the next silo lifecycle in the same
/// process. The throughput bench surfaced this as a producer stuck retrying
/// against a dead Testcontainers Kafka broker between sweep calls.
/// </summary>
public sealed class EdictKafkaAdapterFactoryDisposalTests
{
    const string ProviderName = "edict-kafka";

    static readonly EdictKafkaStreamsOptions Options = new()
    {
        BootstrapServers = "localhost:9092",
        ConsumerGroupId = "edict-kafka-factory-disposal-tests",
        PartitionCount = 4,
    };

    [Fact]
    public async Task Dispose_ShouldFlushAndDisposeTheMaterialisedAdapterProducer()
    {
        var fakeProducer = new FakeKafkaProducer();
        var factory = new EdictKafkaAdapterFactory(
            ProviderName,
            Options,
            new SimpleQueueCacheOptions(),
            BuildSerializer(),
            NullLoggerFactory.Instance,
            new EdictKafkaStreamRegistry(new[] { "OrdersStream" }),
            producerFactory: _ => fakeProducer);

        _ = await factory.CreateAdapter();

        var disposable = Assert.IsAssignableFrom<IDisposable>((object)factory);
        disposable.Dispose();

        Assert.True(fakeProducer.WasFlushed);
        Assert.True(fakeProducer.WasDisposed);
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(s => s
            .AddAssembly(typeof(KafkaWireEnvelope).Assembly)
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
