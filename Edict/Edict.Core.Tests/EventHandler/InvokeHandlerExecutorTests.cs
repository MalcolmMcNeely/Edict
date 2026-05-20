using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Core.EventHandler;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.EventHandler;

// InvokeHandlerExecutor unit test against a fake IOutboxHost (ADR 0023). The
// executor deserialises the entry's EdictEvent payload and routes it back into
// the host grain via IOutboxHost.DispatchEventAsync — the seam the engine
// drives the host's idempotent-consumer dispatch through. No grain, no
// cluster, no backend: the engine's host adapter is replaced by a fake so the
// executor's logic body is the only thing under test.
public sealed class InvokeHandlerExecutorTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ExecuteAsync_ShouldDispatchDeserialisedEventToHost_WhenInvokeHandlerEntry()
    {
        var evt = new OrderPlacedEvent(
            OrderId: new Guid("11111111-1111-1111-1111-111111111111"),
            Sku: "WIDGET")
        {
            EventId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        };
        var entry = new OutboxEntry
        {
            EntryId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(evt),
        };
        var host = new FakeInvokeHandlerHost();

        var executor = new InvokeHandlerExecutor(Serializer);
        await executor.ExecuteAsync(entry, host);

        await Verify(host.Dispatched).DontScrubGuids();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNestInvocationSpanUnderCapturedTraceParent_WhenEntryCarriesTraceParent()
    {
        var capturedTraceId = "0123456789abcdef0123456789abcdef";
        var capturedSpanId = "fedcba9876543210";
        var traceParent = ActivityExtensions.BuildTraceParent(capturedTraceId, capturedSpanId);

        var evt = new OrderPlacedEvent(
            OrderId: new Guid("33333333-3333-3333-3333-333333333333"),
            Sku: "TRACED")
        {
            EventId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        };
        var entry = new OutboxEntry
        {
            EntryId = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(evt),
            TraceParent = traceParent,
            TraceState = null,
        };

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var host = new FakeInvokeHandlerHost();
        var executor = new InvokeHandlerExecutor(Serializer);

        await executor.ExecuteAsync(entry, host);

        var span = Assert.Single(stopped);
        Assert.Equal("edict.event.handle OrderPlacedEvent", span.OperationName);
        Assert.Equal(capturedTraceId, span.TraceId.ToHexString());
        Assert.Equal(capturedSpanId, span.ParentSpanId.ToHexString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSurfaceHostThrow_WhenHandleThrows()
    {
        var evt = new OrderPlacedEvent(
            OrderId: new Guid("22222222-2222-2222-2222-222222222222"),
            Sku: "FAULT")
        {
            EventId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        };
        var entry = new OutboxEntry
        {
            EntryId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(evt),
        };
        var host = new FakeInvokeHandlerHost { ThrowOnDispatch = true };

        var executor = new InvokeHandlerExecutor(Serializer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(entry, host));
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(InvokeHandlerExecutorTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed class FakeInvokeHandlerHost : IOutboxHost
    {
        public List<EdictEvent> Dispatched { get; } = [];
        public bool ThrowOnDispatch { get; init; }

        public OutboxSlice Outbox { get; set; } = new();
        public Orleans.Streams.IStreamProvider StreamProvider => null!;
        public string GrainKey => "00000000-0000-0000-0000-000000000000";
        public string GrainTypeName => "FakeInvokeHandlerHost";

        public Task CommitAsync() => Task.CompletedTask;
        public Task RegisterDrainReminderAsync() => Task.CompletedTask;
        public Task UnregisterDrainReminderAsync() => Task.CompletedTask;

        public Task DispatchEventAsync(EdictEvent evt)
        {
            if (ThrowOnDispatch)
            {
                throw new InvalidOperationException("simulated dispatch failure");
            }

            Dispatched.Add(evt);
            return Task.CompletedTask;
        }
    }
}
