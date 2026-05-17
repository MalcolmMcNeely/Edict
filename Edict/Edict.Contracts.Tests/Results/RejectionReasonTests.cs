using Edict.Contracts.Results;

namespace Edict.Contracts.Tests.Results;

public class RejectionReasonTests
{
    [Fact]
    public void Reasons_with_the_same_code_and_message_are_equal()
    {
        var first = new RejectionReason("out_of_stock", "Item is out of stock.");
        var second = new RejectionReason("out_of_stock", "Item is out of stock.");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Code_and_message_are_addressable_independently()
    {
        var reason = new RejectionReason("out_of_stock", "Item is out of stock.");

        Assert.Equal("out_of_stock", reason.Code);
        Assert.Equal("Item is out of stock.", reason.Message);
    }
}
