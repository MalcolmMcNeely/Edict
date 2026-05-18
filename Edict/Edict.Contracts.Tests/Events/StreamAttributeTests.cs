using Edict.Contracts.Events;

namespace Edict.Contracts.Tests.Events;

public class StreamAttributeTests
{
    [Fact]
    public void StreamAttribute_stores_its_name()
    {
        var attr = new EdictStreamAttribute("Orders");

        Assert.Equal("Orders", attr.Name);
    }
}
