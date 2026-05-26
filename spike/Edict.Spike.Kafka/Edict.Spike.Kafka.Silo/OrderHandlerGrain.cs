using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

using Orleans.Streams;
using Orleans.Streams.Core;

namespace Edict.Spike.Kafka.Silo;

[ImplicitStreamSubscription(SpikeStreamNames.OrdersNamespace)]
public sealed class OrderHandlerGrain : Grain, IGrainWithGuidKey, IStreamSubscriptionObserver
{
    readonly SpikePreCriterionLog _log;
    readonly IGrainFactory _grainFactory;
    readonly ILogger<OrderHandlerGrain> _logger;

    public OrderHandlerGrain(SpikePreCriterionLog log, IGrainFactory grainFactory, ILogger<OrderHandlerGrain> logger)
    {
        _log = log;
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var handle = handleFactory.Create<OrderPlaced>();
        await handle.ResumeAsync(OnNextAsync);
    }

    async Task OnNextAsync(OrderPlaced evt, StreamSequenceToken? token)
    {
        var keyString = this.GetPrimaryKey().ToString();
        _log.Record(SpikeProbeKind.HandleAsyncEnter, keyString, eventId: evt.EventId);

        try
        {
            if (SpikeFaultInjection.ShouldHang(evt.EventId))
            {
                SpikeFaultInjection.SignalEntered(evt.EventId);
                await SpikeFaultInjection.HangForever();
            }

            var recorder = _grainFactory.GetGrain<IRecorderGrain>("orders");
            await recorder.RecordAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderHandlerGrain recorder call failed for event {EventId}", evt.EventId);
            throw;
        }
        finally
        {
            _log.Record(SpikeProbeKind.HandleAsyncExit, keyString, eventId: evt.EventId);
        }
    }
}
