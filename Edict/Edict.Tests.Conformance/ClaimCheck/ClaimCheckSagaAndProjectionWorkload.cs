using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.TableStorage;
using Edict.Core.Commands;
using Edict.Core.Projections;
using Edict.Core.Sagas;
using Edict.Core.TableStorage;

using Orleans;

namespace Edict.Tests.Conformance.ClaimCheck;

// Saga and table-projection consumers subscribed to the same claim-checked
// event stream the counter aggregate raises on. Under the 1-byte claim-check
// threshold, both receive a pointer-bearing envelope and dispatch through the
// deferred InvokeHandler path, so these workloads prove the staged effect
// (a SendCommand from the saga, an UpsertRow from the projection) survives that
// path rather than being silently dropped.

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.ClaimCheck.ClaimCheckSagaProgress")]
public sealed class ClaimCheckSagaProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.ClaimCheck.ClaimCheckSagaTrackerState")]
public sealed class ClaimCheckSagaTrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }
}

public sealed partial record ClaimCheckSagaFollowUpCommand(Guid CounterId) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;
}

public interface IClaimCheckSagaProgressProbe : IGrainWithGuidKey
{
    Task<int> GetHandledAsync();
}

public interface IClaimCheckSagaTrackerProbe : IGrainWithGuidKey
{
    Task<int> GetReceivedAsync();
}

public partial class ClaimCheckSaga : EdictSaga<ClaimCheckSagaProgress>, IClaimCheckSagaProgressProbe
{
    Task HandleAsync(ClaimCheckCounterIncrementedEvent edictEvent)
    {
        Progress.Handled++;
        Dispatch(new ClaimCheckSagaFollowUpCommand(edictEvent.CounterId));
        return Task.CompletedTask;
    }

    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
}

public partial class ClaimCheckSagaFollowUpHandler : EdictCommandHandler<ClaimCheckSagaTrackerState>, IClaimCheckSagaTrackerProbe
{
    Task<EdictCommandResult> HandleAsync(ClaimCheckSagaFollowUpCommand command)
    {
        State.Received++;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<int> GetReceivedAsync() => Task.FromResult(State.Received);
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.ClaimCheck.ClaimCheckProjectionRow")]
public sealed class ClaimCheckProjectionRow : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }

    [Id(1)]
    public string Payload { get; set; } = "";
}

public sealed partial class ClaimCheckProjectionBuilder
    : EdictListProjectionBuilder<ClaimCheckProjectionRow>
{
    public ClaimCheckProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    public const string Table = "claimcheckprojection";

    protected override string TableName => Table;

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            ClaimCheckCounterIncrementedEvent incremented => incremented.CounterId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    Task HandleAsync(ClaimCheckCounterIncrementedEvent edictEvent)
    {
        CurrentRow.Count = edictEvent.NewCount;
        CurrentRow.Payload = edictEvent.Payload;
        return Task.CompletedTask;
    }
}

static class ClaimCheckConsumerWaiters
{
    public static async Task WaitForSagaTrackerAsync(
        IClaimCheckSagaTrackerProbe tracker, int expectedCount = 1, int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await tracker.GetReceivedAsync() >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
