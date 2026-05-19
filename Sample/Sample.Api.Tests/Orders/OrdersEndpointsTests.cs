using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Sample.Api.Tests.Orders;

[Collection(ApiClusterCollection.Name)]
public sealed class OrdersEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task PlaceOrder_ShouldReturn202WithNewOrderId()
    {
        var response = await fixture.Client.PostAsync("/orders", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        Assert.NotEqual(Guid.Empty, body!["orderId"]);
    }

    [Fact]
    public async Task AddLineItem_ShouldReturn202_WhenOrderIsOpen()
    {
        var orderId = await PlaceNewOrder();

        var response = await fixture.Client.PostAsJsonAsync(
            $"/orders/{orderId}/lines",
            new { sku = "ITEM-1", quantity = 2 });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SubmitOrder_ShouldReturn202_WhenOrderHasItems()
    {
        var orderId = await PlaceNewOrder();
        await fixture.Client.PostAsJsonAsync($"/orders/{orderId}/lines", new { sku = "ITEM-1", quantity = 1 });

        var response = await fixture.Client.PostAsync($"/orders/{orderId}/submit", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SubmitOrder_ShouldReturn422WithNoItemsReason_WhenOrderHasNoItems()
    {
        var orderId = await PlaceNewOrder();

        var response = await fixture.Client.PostAsync($"/orders/{orderId}/submit", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var reasons = await response.Content.ReadFromJsonAsync<List<Dictionary<string, string>>>();
        Assert.Single(reasons!);
        Assert.Equal("no_items", reasons![0]["code"]);
    }

    [Fact]
    public async Task CancelOrder_ShouldReturn202()
    {
        var orderId = await PlaceNewOrder();

        var response = await fixture.Client.PostAsync($"/orders/{orderId}/cancel", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_ShouldReturn422WithAlreadySubmittedReason_WhenAlreadySubmitted()
    {
        var orderId = await PlaceNewOrder();
        await fixture.Client.PostAsJsonAsync($"/orders/{orderId}/lines", new { sku = "ITEM-1", quantity = 1 });
        await fixture.Client.PostAsync($"/orders/{orderId}/submit", null);

        var response = await fixture.Client.PostAsync($"/orders/{orderId}/cancel", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var reasons = await response.Content.ReadFromJsonAsync<List<Dictionary<string, string>>>();
        Assert.Single(reasons!);
        Assert.Equal("already_submitted", reasons![0]["code"]);
    }

    private async Task<Guid> PlaceNewOrder()
    {
        var response = await fixture.Client.PostAsync("/orders", null);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        return body!["orderId"];
    }
}
