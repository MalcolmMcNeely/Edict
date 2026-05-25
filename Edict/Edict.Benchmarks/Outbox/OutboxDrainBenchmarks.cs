using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Benchmarks.Outbox;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 5)]
public class OutboxDrainBenchmarks
{
    [Params(1, 5, 50, 500)]
    public int EventsPerPass { get; set; }

    OutboxHost<EdictUnit> _host = null!;
    NoopPersistentState<GrainEnvelope<EdictUnit>> _state = null!;
    OutboxEntry[] _entries = null!;

    [GlobalSetup]
    public void Setup()
    {
        _state = new NoopPersistentState<GrainEnvelope<EdictUnit>>();
        _host = new OutboxHost<EdictUnit>(
            _state,
            NullStreamProvider.Instance,
            new NoopReminderRegistrar(),
            [new NoopExecutor()],
            new EdictOptions(),
            TimeProvider.System,
            new NoopPromoter(),
            grainKey: "bench",
            grainTypeName: "BenchGrain");

        _entries = Enumerable.Range(0, EventsPerPass)
            .Select(i => new OutboxEntry
            {
                EntryId = new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                Kind = OutboxEffectKind.PublishEvent,
                Payload = [(byte)(i & 0xff)],
            })
            .ToArray();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _state.State.Outbox = new OutboxSlice();
        foreach (var entry in _entries)
        {
            _state.State.Outbox = _state.State.Outbox.Enqueue(entry);
        }
    }

    [Benchmark]
    public Task DrainPass() => _host.DrainAsync();

    sealed class NoopPersistentState<T> : IPersistentState<T> where T : new()
    {
        public T State { get; set; } = new();
        public string Etag => string.Empty;
        public bool RecordExists => true;
        public Task WriteStateAsync() => Task.CompletedTask;
        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
    }

    sealed class NoopReminderRegistrar : IReminderRegistrar
    {
        public Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period) => Task.CompletedTask;
        public Task UnregisterReminderAsync(string name) => Task.CompletedTask;
    }

    sealed class NoopExecutor : IOutboxEffectExecutor
    {
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;
        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType) =>
            Task.CompletedTask;
    }

    sealed class NoopPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now) =>
            failed;
    }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;
        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException();
    }
}
