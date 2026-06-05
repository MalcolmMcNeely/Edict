using Microsoft.Extensions.DependencyInjection;

namespace Sample.Domain.Orders.Notifications;

/// <summary>
/// Silo half of the notifications wiring: one call registers the
/// <see cref="HttpEmailNotifier"/> as the silo's <see cref="IEmailNotifier"/>
/// behind a typed <see cref="HttpClient"/> pointed at the Web via Aspire service
/// discovery. The Web half lives next to the sink in <c>Sample.Web.Components</c>;
/// both substrates' silos call this so Azure and Kafka+Postgres stay in parity.
/// </summary>
public static class NotificationsClientRegistration
{
    // Resolved by Aspire service discovery to the "web" resource. AddServiceDefaults
    // already wires discovery + the standard resilience handler onto every HttpClient,
    // so a transient Web-down surfaces here as the swallowed exception the notifier expects.
    public static IServiceCollection AddHttpEmailNotifier(this IServiceCollection services)
    {
        services.AddHttpClient<IEmailNotifier, HttpEmailNotifier>(client =>
            client.BaseAddress = new Uri("https+http://web"));
        return services;
    }
}
