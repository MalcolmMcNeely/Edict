using System.Diagnostics;

using Edict.Azure.Persistence.TableStorage;
using Edict.Telemetry;

namespace Edict.Azure.Tests.Outbox;

// The all-zero traceparent trap: when the command runs with no active
// Activity, the stamped event must carry null trace ids — a synthesised
// all-zero 32-char hex string fails ActivityTraceId.CreateFromString on the
// consumer side and silently breaks delivery to every EdictIdempotencyBase
// consumer (projections, sagas, event handlers).
[Collection(AzureClusterCollection.Name)]
public sealed class OutboxTraceContextTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task NoActiveActivity_ShouldNotSynthesiseAllZeroTraceparent_AndProjectionStillReceivesEvent()
    {
        // The trap fires precisely when no parent context exists; clearing
        // Activity.Current ensures the command-side trace capture stores null
        // rather than an ambient id from the test runner.
        Activity.Current = null;

        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureorderprojection");

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-NULLTRACE"));

        await WaitForRowAsync(repository, orderId.ToString(), orderId.ToString());
        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        // The projection IS an EdictIdempotencyBase consumer — a written row
        // proves the dispatcher did not throw on a malformed traceparent.
        Assert.NotNull(row);
        Assert.Equal(1, row!.OrderCount);
    }

    [Fact]
    public async Task ActiveActivity_ShouldPropagateTraceContext_ToConsumerSpan()
    {
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

        var publishSpan = stopped.FirstOrDefault(a =>
            a.OperationName.StartsWith($"{SemanticConventions.Events.Spans.Publish} ", StringComparison.Ordinal)
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
