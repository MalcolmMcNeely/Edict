using Edict.Contracts.Events;

namespace Edict.Contracts.Tests.Events;

public class StreamAttributeTests
{
    [Fact]
    public void EdictStreamAttribute_ShouldStoreItsName()
    {
        var attr = new EdictStreamAttribute("Orders");

        Assert.Equal("Orders", attr.Name);
    }
}
