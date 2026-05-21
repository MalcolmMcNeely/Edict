using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace Sample.Api.Tests.Orders;

/// <summary>
/// Exercises the OrderPayment saga end-to-end through the Api over real Azurite
///: the happy path (PaymentAuthorized → ConfirmOrder) and the
/// compensation branch (PaymentDeclined → CancelOrder), selected by the submit
/// amount against PaymentCommandHandler.DeclineThreshold.
/// </summary>
[Collection(ApiClusterCollection.Name)]
public sealed class OrderPaymentWorkflowTests(ApiFixture fixture)
{
    [Fact]
    public async Task Workflow_ShouldConfirmOrder_WhenPaymentAuthorized()
    {
        var orderId = await SubmitOrder(amount: 250m);

        var outcome = await PollOutcomeUntilTerminal(orderId);

        Assert.Equal("Confirmed", outcome);
    }

    [Fact]
    public async Task Workflow_ShouldCancelOrder_WhenPaymentDeclined()
    {
        var orderId = await SubmitOrder(amount: 5000m);

        var outcome = await PollOutcomeUntilTerminal(orderId);

        Assert.Equal("Cancelled", outcome);
    }

    private async Task<Guid> SubmitOrder(decimal amount)
    {
        var place = await fixture.Client.PostAsync("/orders", null);
        var body = await place.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        var orderId = body!["orderId"];

        await fixture.Client.PostAsJsonAsync(
            $"/orders/{orderId}/lines", new { sku = "ITEM-1", quantity = 1 });
        var submit = await fixture.Client.PostAsync(
            $"/orders/{orderId}/submit?amount={amount}", null);
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        return orderId;
    }

    private async Task<string> PollOutcomeUntilTerminal(Guid orderId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await fixture.Client.GetAsync($"/orders/{orderId}/outcome");
            if (response.IsSuccessStatusCode)
            {
                var outcome = await response.Content.ReadAsStringAsync();
                if (outcome is "Confirmed" or "Cancelled")
                    return outcome;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return "";
    }
}
