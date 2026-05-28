using Edict.Core.Outbox;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;
using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

public sealed class UpsertRowExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldDeserialiseRowViaResolvedAlias_AndReachFactoryWithConcreteType()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b => b.AddAssembly(typeof(UpsertRowExecutorTests).Assembly));
        var capturingFactory = new CapturingTableStoreFactory();
        services.AddSingleton<IEdictTableStoreFactory>(capturingFactory);
        var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<Serializer>();
        var rowSerializer = provider.GetRequiredService<ObjectSerializer>();
        var typeConverter = provider.GetRequiredService<TypeConverter>();
        var resolver = new RowTypeResolver(typeConverter);

        var row = new KnownAliasedRow { Value = "round-trip" };
        var effect = new UpsertRowEffect
        {
            TableName = "test-rows",
            PartitionKey = "pk",
            RowKey = "rk",
            RowAlias = typeConverter.Format(typeof(KnownAliasedRow)),
            RowBytes = serializer.SerializeToArray<object>(row),
        };
        var entry = new OutboxEntry
        {
            EntryId = new Guid("11111111-1111-1111-1111-111111111111"),
            Kind = OutboxEffectKind.UpsertRow,
            Payload = serializer.SerializeToArray(effect),
        };

        var executor = new UpsertRowExecutor(serializer, rowSerializer, resolver, provider);

        await executor.ExecuteAsync(
            entry,
            NullStreamProvider.Instance,
            deferredDispatch: null,
            consumerType: null,
            liveWireEvent: null);

        Assert.NotNull(capturingFactory.LastRow);
        Assert.IsType<KnownAliasedRow>(capturingFactory.LastRow);
        Assert.Equal(row, capturingFactory.LastRow);
        Assert.Equal("test-rows", capturingFactory.LastTableName);
        Assert.Equal("pk", capturingFactory.LastPartitionKey);
        Assert.Equal("rk", capturingFactory.LastRowKey);
    }

    sealed class CapturingTableStoreFactory : IEdictTableStoreFactory
    {
        public string? LastTableName { get; private set; }
        public string? LastPartitionKey { get; private set; }
        public string? LastRowKey { get; private set; }
        public object? LastRow { get; private set; }

        public Task<Contracts.TableStorage.IEdictTableWriteStore<T>> CreateAsync<T>(
            string tableName,
            CancellationToken cancellationToken = default) where T : class, new() =>
            throw new NotSupportedException("UpsertRowExecutor never calls CreateAsync<T>.");

        public Task UpsertRowAsync(
            string tableName,
            string partitionKey,
            string rowKey,
            object row,
            CancellationToken cancellationToken = default)
        {
            LastTableName = tableName;
            LastPartitionKey = partitionKey;
            LastRowKey = rowKey;
            LastRow = row;
            return Task.CompletedTask;
        }
    }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;

        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException("NullStreamProvider has no streams.");
    }
}
