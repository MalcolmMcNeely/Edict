using Edict.Contracts.Events;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.TestSupport;

// Records every EdictEvent handed to OnNextAsync / OnNextBatchAsync so a test
// can assert what the PublishEvent executor put on the wire. One stream per
// StreamId; the same StreamId always returns the same capturing stream so a
// re-drain of the same entry lands on the stream the test already holds.
sealed class CapturingStreamProvider : IStreamProvider
{
    readonly Dictionary<StreamId, CapturingAsyncStream> _streams = [];

    public string Name => "edict";
    public bool IsRewindable => false;

    public IReadOnlyList<EdictEvent> Captured =>
        _streams.Values.SelectMany(stream => stream.Captured).ToArray();

    public IAsyncStream<T> GetStream<T>(StreamId streamId)
    {
        if (!_streams.TryGetValue(streamId, out var stream))
        {
            stream = new CapturingAsyncStream(streamId);
            _streams[streamId] = stream;
        }
        return (IAsyncStream<T>)(object)stream;
    }
}

sealed class CapturingAsyncStream(StreamId streamId) : IAsyncStream<EdictEvent>
{
    readonly List<EdictEvent> _captured = [];

    public IReadOnlyList<EdictEvent> Captured => _captured;

    public StreamId StreamId => streamId;
    public string ProviderName => "edict";
    public bool IsRewindable => false;

    public Task OnNextAsync(EdictEvent item, StreamSequenceToken? token = null)
    {
        _captured.Add(item);
        return Task.CompletedTask;
    }

    public Task OnNextBatchAsync(IEnumerable<EdictEvent> batch, StreamSequenceToken? token = null)
    {
        _captured.AddRange(batch);
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync() => Task.CompletedTask;
    public Task OnErrorAsync(Exception exception) => Task.CompletedTask;

    public Task<StreamSubscriptionHandle<EdictEvent>> SubscribeAsync(IAsyncObserver<EdictEvent> observer) =>
        throw new NotSupportedException("capturing stream is producer-only");

    public Task<StreamSubscriptionHandle<EdictEvent>> SubscribeAsync(
        IAsyncObserver<EdictEvent> observer, StreamSequenceToken? token, string? filterData = null) =>
        throw new NotSupportedException("capturing stream is producer-only");

    public Task<StreamSubscriptionHandle<EdictEvent>> SubscribeAsync(IAsyncBatchObserver<EdictEvent> observer) =>
        throw new NotSupportedException("capturing stream is producer-only");

    public Task<StreamSubscriptionHandle<EdictEvent>> SubscribeAsync(
        IAsyncBatchObserver<EdictEvent> observer, StreamSequenceToken? token) =>
        throw new NotSupportedException("capturing stream is producer-only");

    public Task<IList<StreamSubscriptionHandle<EdictEvent>>> GetAllSubscriptionHandles() =>
        throw new NotSupportedException("capturing stream is producer-only");

    public bool Equals(IAsyncStream<EdictEvent>? other) => ReferenceEquals(this, other);
    public int CompareTo(IAsyncStream<EdictEvent>? other) => ReferenceEquals(this, other) ? 0 : 1;
}
