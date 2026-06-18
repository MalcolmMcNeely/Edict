using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Microsoft.Extensions.Time.Testing;

using Orleans.Hosting;

namespace Edict.Benchmarks.Throughput.Tests;

/// <summary>
/// Clock-free coverage of the bring-up policy: a stub step delegate (succeeds,
/// fails, or delays against the virtual clock) plus a <see cref="FakeTimeProvider"/>
/// prove the retry/stagger/timeout/dispose behaviour with no real Docker, mirroring
/// the pure-unit <c>ClusterContextRegistryTests</c> and the existing
/// virtual-clock prior art.
/// </summary>
public sealed class SubstrateBringUpPolicyTests
{
    [Fact]
    public async Task BringUpAsync_SucceedsOnFirstAttempt_ReturnsRuntimeWithoutDisposingTheStep()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var policy = new SubstrateBringUpPolicy(time);
        var container = new TrackingDisposable();
        var runtime = new StubSubstrateRuntime();
        var attempts = 0;
        SubstrateBringUpStep step = (_, _) =>
        {
            attempts++;
            return Task.FromResult<IAsyncDisposable>(container);
        };

        // Act
        var result = await policy.BringUpAsync(
            "azure", [step], _ => runtime, Tuning(retryCount: 3, staggerSeconds: 0), CancellationToken.None);

        // Assert
        Assert.Same(runtime, result);
        Assert.Equal(1, attempts);
        Assert.False(container.Disposed);
    }

    [Fact]
    public async Task BringUpAsync_StepAlwaysFails_RetriesConfiguredCountThenThrowsCarryingLastFailure()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var policy = new SubstrateBringUpPolicy(time);
        var failure = new InvalidOperationException("host probe stalled");
        var attempts = 0;
        SubstrateBringUpStep step = (_, _) =>
        {
            attempts++;
            throw failure;
        };

        // Act
        var pending = policy.BringUpAsync(
            "azure", [step], _ => new StubSubstrateRuntime(), Tuning(retryCount: 3, staggerSeconds: 0), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<SubstrateBringUpFailedException>(() => DriveAsync(pending, time));

        // Assert
        Assert.Equal(3, attempts);
        Assert.Equal("azure", exception.SubstrateName);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task BringUpAsync_RunsStepsInDeclaredOrder()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var policy = new SubstrateBringUpPolicy(time);
        var order = new List<int>();
        SubstrateBringUpStep MakeStep(int index) => (_, _) =>
        {
            order.Add(index);
            return Task.FromResult<IAsyncDisposable>(new TrackingDisposable());
        };

        // Act
        await policy.BringUpAsync(
            "kafkapostgres",
            [MakeStep(0), MakeStep(1), MakeStep(2)],
            _ => new StubSubstrateRuntime(),
            Tuning(retryCount: 1, staggerSeconds: 0),
            CancellationToken.None);

        // Assert
        Assert.Equal([0, 1, 2], order);
    }

    [Fact]
    public async Task BringUpAsync_StepExceedsPerAttemptBudget_FailsTheAttempt()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var policy = new SubstrateBringUpPolicy(time);
        SubstrateBringUpStep step = async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromHours(1), time, cancellationToken);
            return new TrackingDisposable();
        };

        // Act — the per-attempt budget is wait + probe = 2 s; the step parks for an hour.
        var pending = policy.BringUpAsync(
            "azure",
            [step],
            _ => new StubSubstrateRuntime(),
            Tuning(retryCount: 1, staggerSeconds: 0, waitSeconds: 1, probeSeconds: 1),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<SubstrateBringUpFailedException>(() => DriveAsync(pending, time));

        // Assert
        Assert.IsType<TimeoutException>(exception.InnerException);
    }

    [Fact]
    public async Task BringUpAsync_CallerCancelsMidBringUp_AbortsPromptlyWithoutBackoff()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var policy = new SubstrateBringUpPolicy(time);
        using var cancellation = new CancellationTokenSource();
        SubstrateBringUpStep step = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, time, cancellationToken);
            return new TrackingDisposable();
        };

        // Act
        var pending = policy.BringUpAsync(
            "azure",
            [step],
            _ => new StubSubstrateRuntime(),
            Tuning(retryCount: 3, staggerSeconds: 0, waitSeconds: 300, probeSeconds: 300),
            cancellation.Token);
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();

        // Assert — no clock advance, so only the caller's cancellation could complete it.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task BringUpAsync_DisposesEveryStartedStep_OnAFailurePath()
    {
        // Arrange — two steps start, the third fails this attempt.
        var time = new FakeTimeProvider();
        var policy = new SubstrateBringUpPolicy(time);
        var first = new TrackingDisposable();
        var second = new TrackingDisposable();
        SubstrateBringUpStep startsFirst = (_, _) => Task.FromResult<IAsyncDisposable>(first);
        SubstrateBringUpStep startsSecond = (_, _) => Task.FromResult<IAsyncDisposable>(second);
        SubstrateBringUpStep fails = (_, _) => throw new InvalidOperationException("third container stalled");

        // Act
        var pending = policy.BringUpAsync(
            "kafkapostgres",
            [startsFirst, startsSecond, fails],
            _ => new StubSubstrateRuntime(),
            Tuning(retryCount: 1, staggerSeconds: 0),
            CancellationToken.None);
        await Assert.ThrowsAsync<SubstrateBringUpFailedException>(() => DriveAsync(pending, time));

        // Assert
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task BringUpAsync_BackoffElapsesAgainstInjectedClock_NotWallClock()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var policy = new SubstrateBringUpPolicy(time);
        var attempts = 0;
        SubstrateBringUpStep step = (_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("stalled");
        };

        // Act — first attempt runs synchronously, then the policy parks on the backoff.
        var pending = policy.BringUpAsync(
            "azure", [step], _ => new StubSubstrateRuntime(), Tuning(retryCount: 2, staggerSeconds: 0), CancellationToken.None);

        // Assert — wall-clock time passing does not advance the virtual clock, so the
        // retry stays parked until the injected clock is advanced.
        Assert.Equal(1, attempts);
        Assert.False(pending.IsCompleted);

        await Assert.ThrowsAsync<SubstrateBringUpFailedException>(() => DriveAsync(pending, time));
        Assert.Equal(2, attempts);
    }

    static BringUpTuning Tuning(int retryCount, int staggerSeconds, int waitSeconds = 300, int probeSeconds = 300) =>
        new()
        {
            RetryCount = retryCount,
            WithinBootStaggerGap = TimeSpan.FromSeconds(staggerSeconds),
            TestcontainersWaitTimeout = TimeSpan.FromSeconds(waitSeconds),
            HostReadinessProbeDeadline = TimeSpan.FromSeconds(probeSeconds),
        };

    // Pumps the parked policy by advancing the virtual clock a generous tick per
    // iteration, yielding so the elapsed delay's continuation runs and registers
    // the next one. Each delay (backoff, stagger, per-step budget) is well under a
    // minute, so the loop converges in a handful of ticks.
    static async Task<ISubstrateRuntime> DriveAsync(Task<ISubstrateRuntime> task, FakeTimeProvider time)
    {
        for (var tick = 0; tick < 1000 && !task.IsCompleted; tick++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            await Task.Yield();
        }
        return await task;
    }

    sealed class TrackingDisposable : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    sealed class StubSubstrateRuntime : ISubstrateRuntime
    {
        public Action<ISiloBuilder> ConfigureSilo => _ => { };

        public Action<IClientBuilder> ConfigureClient => _ => { };

        public IEdictTableWriteStore<TRow> CreateRowStore<TRow>(IServiceProvider serviceProvider, string tableName)
            where TRow : class, new() =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
