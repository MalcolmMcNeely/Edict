using Edict.Contracts.Commands;
using Edict.Contracts.Sending;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sample.Contracts.Orders.Commands;

namespace Sample.Web.Components.Simulator;

/// <summary>
/// Demo prop, not a framework primitive. A web-tier hosted-service singleton
/// that places random 1–5 line orders on a 2-second cadence when running.
/// Paused by default; the hub's ▶ / ⏸ buttons toggle <see cref="Start"/> and
/// <see cref="Stop"/>. State (the running flag, the ticking task) is shared
/// across Blazor circuits because the service is a singleton. No persistence
/// across web restart — the simulator is a sales-demo affordance, not a
/// background scheduler.
/// </summary>
public sealed class OrderSimulatorService : IHostedService, IDisposable
{
    static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);
    static readonly string[] SampleSkus = ["SKU-1", "SKU-2", "SKU-3", "SKU-4", "SKU-5"];

    readonly IEdictSender _sender;
    readonly KnownOrdersRegistry _knownOrders;
    readonly ILogger<OrderSimulatorService> _logger;
    readonly Random _random = new();

    CancellationTokenSource? _runCts;
    Task? _runLoop;

    public OrderSimulatorService(
        IEdictSender sender,
        KnownOrdersRegistry knownOrders,
        ILogger<OrderSimulatorService> logger)
    {
        _sender = sender;
        _knownOrders = knownOrders;
        _logger = logger;
    }

    public bool IsRunning => _runLoop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _runCts = new CancellationTokenSource();
        _runLoop = Task.Run(() => RunAsync(_runCts.Token));
    }

    public void Stop()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
    }

    async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await PlaceOneRandomOrderAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "OrderSimulator tick failed; the simulator will keep ticking.");
        }
    }

    async Task PlaceOneRandomOrderAsync()
    {
        var orderId = Guid.NewGuid();
        var lineCount = _random.Next(1, 6);
        var amount = 50m + _random.Next(0, 100);

        var place = await _sender.Send(new PlaceOrderCommand(orderId, "SIM-" + orderId.ToString("N")[..6]));
        if (place is not EdictCommandResult.Accepted)
        {
            return;
        }

        _knownOrders.Register(orderId);

        for (var i = 0; i < lineCount; i++)
        {
            var sku = SampleSkus[_random.Next(SampleSkus.Length)];
            await _sender.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), sku, 1));
        }

        await _sender.Send(new SubmitOrderCommand(orderId, amount));
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Stop();
        _runLoop = null;
    }
}
