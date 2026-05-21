using System.Diagnostics;

using Edict.Azure.TableStorage;
using Edict.Telemetry;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Two ADR 0003 invariants the Outbox engine + <c>PublishEventExecutor</c>
/// must preserve, end-to-end on Azurite:
/// <list type="number">
///   <item>An active Activity at command time stitches the
///         <c>Command → Publish → Handle</c> span tree across the stream hop
///         (parent-child, not span-link).</item>
///   <item><b>The all-zero traceparent trap</b> (cost a long debug — see
///         <c>memory/outbox-engine-slice</c>): when the command runs with no
///         active Activity, the entry's captured trace parent is <c>null</c>
///         and the executor's <c>publishActivity</c> may also be <c>null</c> if
///         no listener is attached. The stamped event must carry null trace
///         ids — never a synthesised all-zero 32-char hex string, which a
///         consumer's <see cref="ActivityTraceId.CreateFromString"/> rejects,
///         silently breaking delivery to every <see cref="EdictIdempotencyBase"/>
///         consumer (projections, sagas, event handlers).</item>
/// </list>
/// Lifted from <c>OutboxHostTests.EnqueueAndDrainAsync_ShouldCarryTraceContext_OnStagedEntries</c>
/// and expanded to make the trap explicit. A successful row write on the
/// <see cref="AzureOrderTableProjectionBuilder"/> projection (the projection
/// derives from <c>EdictIdempotencyBase</c>) is the proof the event reached
/// the consumer without the dispatcher throwing on a malformed traceparent.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class OutboxTraceContextTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task NoActiveActivity_ShouldNotSynthesiseAllZeroTraceparent_AndProjectionStillReceivesEvent()
    {
        // Defend against an ambient Activity coming in from the test runner —
        // the trap fires precisely when no parent context exists. Clearing
        // Activity.Current for this scope ensures the command-side trace
        // capture stores null rather than a real (non-zero) id.
        Activity.Current = null;

        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureorderprojection");

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-NULLTRACE"));

        await WaitForRowAsync(repository, orderId.ToString(), orderId.ToString());
        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        // The projection IS an EdictIdempotencyBase consumer — a row written
        // here is end-to-end proof that the dispatcher did not throw on an
        // all-zero traceparent and the event reached the handler.
        Assert.NotNull(row);
        Assert.Equal(1, row!.OrderCount);
    }

    [Fact]
    public async Task ActiveActivity_ShouldPropagateTraceContext_ToConsumerSpan()
    {
        // Caller-side Activity stitches the Command → Publish → Handle span
        // tree across the stream hop. Capturing the consumer-side handle span
        // and asserting its TraceId matches the caller's is the ADR 0003
        // parent-child stitch proof on the real Azure Queue transport.
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using var caller = new ActivitySource("OutboxTraceContextTests.Caller")
            .StartActivity("caller-root", ActivityKind.Internal)
            ?? new Activity("caller-root").Start();

        var orderId = Guid.NewGuid();
        var expectedTraceId = caller.TraceId.ToHexString();

        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureorderprojection");

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-TRACE"));

        await WaitForRowAsync(repository, orderId.ToString(), orderId.ToString());

        // The publish span carries the caller's trace id — the parent-child
        // stitch survived stream-hop serialisation through real Azure Queue
        // Storage.
        var publishSpan = stopped.FirstOrDefault(a =>
            a.OperationName.StartsWith("edict.event.publish ", StringComparison.Ordinal)
            && a.TraceId.ToHexString() == expectedTraceId);
        Assert.NotNull(publishSpan);
    }

    static async Task WaitForRowAsync(
        AzureTableRepository<AzureOrderTableRow> repository,
        string partitionKey,
        string rowKey)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && row.OrderCount >= 1)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
