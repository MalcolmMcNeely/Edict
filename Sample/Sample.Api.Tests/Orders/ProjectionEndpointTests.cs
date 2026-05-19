using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace Sample.Api.Tests.Orders;

[Collection(ApiClusterCollection.Name)]
public sealed class ProjectionEndpointTests(ApiFixture fixture)
{
    // Cycle 1 — tracer bullet: placed order appears as Open
    [Fact]
    public async Task PlacedOrder_ShouldAppearAsOpenInProjection()
    {
        var orderId = await PlaceNewOrder();

        var html = await PollProjectionUntil(orderId, html => html.Contains("Open"));

        Assert.Contains("Open", html);
    }

    // Cycle 2 — submitted order transitions to Submitted
    [Fact]
    public async Task SubmittedOrder_ShouldAppearAsSubmittedInProjection()
    {
        var orderId = await PlaceNewOrder();
        await fixture.Client.PostAsJsonAsync(
            $"/orders/{orderId}/lines", new { sku = "ITEM-1", quantity = 1 });
        await fixture.Client.PostAsync($"/orders/{orderId}/submit", null);

        var html = await PollProjectionUntil(orderId, html => html.Contains("Submitted"));

        Assert.Contains("Submitted", html);
    }

    // Cycle 3 — cancelled order transitions to Cancelled
    [Fact]
    public async Task CancelledOrder_ShouldAppearAsCancelledInProjection()
    {
        var orderId = await PlaceNewOrder();
        await fixture.Client.PostAsync($"/orders/{orderId}/cancel", null);

        var html = await PollProjectionUntil(orderId, html => html.Contains("Cancelled"));

        Assert.Contains("Cancelled", html);
    }

    // Cycle 4 — adding a line item increments the item count
    [Fact]
    public async Task AddLineItem_ShouldIncrementItemCountInProjection()
    {
        var orderId = await PlaceNewOrder();
        await fixture.Client.PostAsJsonAsync(
            $"/orders/{orderId}/lines", new { sku = "ITEM-1", quantity = 3 });

        var html = await PollProjectionUntil(orderId, html => html.Contains(">1<"));

        Assert.Contains(">1<", html);
    }

    private async Task<Guid> PlaceNewOrder()
    {
        var response = await fixture.Client.PostAsync("/orders", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        return body!["orderId"];
    }

    private async Task<string> PollProjectionUntil(Guid orderId, Func<string, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await fixture.Client.GetAsync($"/orders/{orderId}/projection");
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                if (condition(html))
                    return html;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return "";
    }
}
