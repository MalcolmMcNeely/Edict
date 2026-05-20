using Edict.Contracts.Commands;

namespace Edict.Contracts.Tests.Commands;

public class RejectionReasonTests
{
    [Fact]
    public void EdictRejectionReason_ShouldBeEqual_WhenSameCodeAndMessage()
    {
        var first = new EdictRejectionReason("out_of_stock", "Item is out of stock.");
        var second = new EdictRejectionReason("out_of_stock", "Item is out of stock.");

        Assert.Equal(first, second);
    }

    [Fact]
    public void EdictRejectionReason_ShouldAddressCodeAndMessageIndependently()
    {
        var reason = new EdictRejectionReason("out_of_stock", "Item is out of stock.");

        Assert.Equal("out_of_stock", reason.Code);
        Assert.Equal("Item is out of stock.", reason.Message);
    }
}
