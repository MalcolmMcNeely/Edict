using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.Handlers;
using Edict.Mcp.Versioning;

namespace Edict.Mcp.Tools;

sealed class ListRouteKeysTool
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly Func<CancellationToken, Task<HandlerInventory>> inventoryProvider;
    readonly Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider;

    public ListRouteKeysTool(
        Func<CancellationToken, Task<HandlerInventory>> inventoryProvider,
        Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider)
    {
        this.inventoryProvider = inventoryProvider;
        this.versionReportProvider = versionReportProvider;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var inventory = await inventoryProvider(cancellationToken);
        var versionReport = await versionReportProvider(cancellationToken);
        var view = BuildView(inventory, versionReport.DriftStatus);
        return JsonSerializer.Serialize(view, JsonOptions);
    }

    static RouteKeysView BuildView(HandlerInventory inventory, string driftStatus)
    {
        var commandBindings = new SortedDictionary<string, CommandBindingBuilder>(StringComparer.Ordinal);
        var eventBindings = new SortedDictionary<string, EventBindingBuilder>(StringComparer.Ordinal);

        foreach (var handler in inventory.Handlers)
        {
            foreach (var contract in handler.BoundContracts)
            {
                if (handler.Role == HandlerRole.CommandHandler)
                {
                    if (!commandBindings.TryGetValue(contract.FullTypeName, out var builder))
                    {
                        builder = new CommandBindingBuilder(contract.FullTypeName, contract.RouteKeyPropertyName);
                        commandBindings[contract.FullTypeName] = builder;
                    }
                    builder.Handlers.Add(handler.DeclaringTypeName);
                }
                else
                {
                    if (!eventBindings.TryGetValue(contract.FullTypeName, out var builder))
                    {
                        builder = new EventBindingBuilder(contract.FullTypeName, contract.RouteKeyPropertyName);
                        eventBindings[contract.FullTypeName] = builder;
                    }
                    builder.Subscribers.Add(handler.DeclaringTypeName);
                }
            }
        }

        var commands = commandBindings.Values
            .Select(builder => new CommandRouteEntry(
                CommandType: builder.CommandType,
                RouteKeyProperty: builder.RouteKeyProperty,
                Handlers: builder.Handlers.OrderBy(name => name, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var events = eventBindings.Values
            .Select(builder => new EventRouteEntry(
                EventType: builder.EventType,
                RouteKeyProperty: builder.RouteKeyProperty,
                Subscribers: builder.Subscribers.OrderBy(name => name, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var collisions = commands
            .Where(entry => entry.Handlers.Count > 1)
            .Select(entry => new CommandCollision(entry.CommandType, entry.Handlers))
            .ToArray();

        return new RouteKeysView(commands, events, collisions, driftStatus);
    }

    sealed class CommandBindingBuilder
    {
        public CommandBindingBuilder(string commandType, string? routeKeyProperty)
        {
            CommandType = commandType;
            RouteKeyProperty = routeKeyProperty;
        }
        public string CommandType { get; }
        public string? RouteKeyProperty { get; }
        public HashSet<string> Handlers { get; } = new(StringComparer.Ordinal);
    }

    sealed class EventBindingBuilder
    {
        public EventBindingBuilder(string eventType, string? routeKeyProperty)
        {
            EventType = eventType;
            RouteKeyProperty = routeKeyProperty;
        }
        public string EventType { get; }
        public string? RouteKeyProperty { get; }
        public HashSet<string> Subscribers { get; } = new(StringComparer.Ordinal);
    }

    sealed record RouteKeysView(
        IReadOnlyList<CommandRouteEntry> Commands,
        IReadOnlyList<EventRouteEntry> Events,
        IReadOnlyList<CommandCollision> Collisions,
        string DriftStatus);

    sealed record CommandRouteEntry(string CommandType, string? RouteKeyProperty, IReadOnlyList<string> Handlers);
    sealed record EventRouteEntry(string EventType, string? RouteKeyProperty, IReadOnlyList<string> Subscribers);
    sealed record CommandCollision(string CommandType, IReadOnlyList<string> Handlers);
}
