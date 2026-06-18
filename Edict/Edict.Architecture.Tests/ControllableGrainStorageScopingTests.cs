using Edict.Tests.Conformance.Outbox;

using Orleans.Runtime;
using Orleans.Storage;

using Xunit;

namespace Edict.Architecture.Tests;

// Locks the scoping contract behind ADR-0068's count-addressed fault seams: when a
// StorageFaultState names a target grain, ControllableGrainStorage faults only that
// grain's writes and passes every peer grain's write through untouched. Without
// this, the silo-wide count is stolen by a peer grain's asynchronous write (an
// EdictEventHandler's dedup commit), which is the exact flake the deterministic
// scenarios exist to avoid — invisible to in-memory fakes, rare under real backend
// latency. A pure-logic test (no cluster, no container) so the contract is pinned
// directly rather than proven probabilistically by a scenario no longer flaking.
public class ControllableGrainStorageScopingTests
{
    static readonly GrainId TargetGrain = GrainId.Create("counteraggregate", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    static readonly GrainId PeerGrain = GrainId.Create("counterappliedeventhandler", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    [Fact]
    public async Task CountAddressedFault_ScopedToTargetGrain_IsNotConsumedByPeerGrainWrites()
    {
        var inner = new RecordingGrainStorage();
        var fault = new StorageFaultState
        {
            TargetGrainKey = TargetGrain.Key.ToString(),
            FailUntilWrite = 1,
        };
        var storage = new ControllableGrainStorage(inner, fault);

        // A peer grain's write must pass through — it never consumes the fault.
        await storage.WriteStateAsync("state", PeerGrain, NewState());
        Assert.Equal(0, fault.FailedWrites);
        Assert.Contains(PeerGrain, inner.Written);

        // The target grain's first write faults (the count-addressed fault).
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.WriteStateAsync("state", TargetGrain, NewState()));
        Assert.Equal(1, fault.FailedWrites);

        // The target grain's second write heals — the count is spent.
        await storage.WriteStateAsync("state", TargetGrain, NewState());
        Assert.Contains(TargetGrain, inner.Written);
    }

    [Fact]
    public async Task UnboundedFault_ScopedToTargetGrain_OnlyFaultsTheTargetGrain()
    {
        var inner = new RecordingGrainStorage();
        var fault = new StorageFaultState
        {
            TargetGrainKey = TargetGrain.Key.ToString(),
            ShouldFailWrites = true,
        };
        var storage = new ControllableGrainStorage(inner, fault);

        // Every peer write passes through even while the unbounded fault is armed.
        await storage.WriteStateAsync("state", PeerGrain, NewState());
        await storage.WriteStateAsync("state", PeerGrain, NewState());
        Assert.Equal(0, fault.FailedWrites);

        // The target grain faults on every write while armed.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.WriteStateAsync("state", TargetGrain, NewState()));
    }

    [Fact]
    public async Task UntargetedFault_FaultsEverySiloWrite()
    {
        var inner = new RecordingGrainStorage();
        var fault = new StorageFaultState { ShouldFailWrites = true };
        var storage = new ControllableGrainStorage(inner, fault);

        // No target named: the historical silo-wide behaviour faults any grain.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.WriteStateAsync("state", PeerGrain, NewState()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.WriteStateAsync("state", TargetGrain, NewState()));
    }

    static GrainState NewState() => new();

    sealed class GrainState : IGrainState<CounterState>
    {
        public CounterState State { get; set; } = new();
        public string? ETag { get; set; }
        public bool RecordExists { get; set; }
    }

    sealed class RecordingGrainStorage : IGrainStorage
    {
        public List<GrainId> Written { get; } = [];

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            Written.Add(grainId);
            return Task.CompletedTask;
        }

        public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) =>
            Task.CompletedTask;

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) =>
            Task.CompletedTask;
    }
}
