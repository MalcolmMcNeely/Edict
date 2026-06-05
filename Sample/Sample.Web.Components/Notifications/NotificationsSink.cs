using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Sample.Domain.Orders.Notifications;

namespace Sample.Web.Components.Notifications;

/// <summary>
/// Web half of the notifications wiring: hosts the in-memory sink the silo POSTs
/// to and the Orders view reads back in-process. <see cref="AddNotificationsSink"/>
/// registers the host-singleton store; <see cref="MapNotificationsSink"/> maps the
/// append endpoint the silo's <c>HttpEmailNotifier</c> calls. Both substrates'
/// Web hosts call these so Azure and Kafka+Postgres stay in parity.
/// </summary>
public static class NotificationsSink
{
    public static IServiceCollection AddNotificationsSink(this IServiceCollection services)
    {
        services.AddSingleton<INotificationsStore, InMemoryNotificationsStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsSink(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/notifications", (SentNotification notification, INotificationsStore store) =>
        {
            store.Append(notification);
            return Results.Accepted();
        });
        return endpoints;
    }
}
