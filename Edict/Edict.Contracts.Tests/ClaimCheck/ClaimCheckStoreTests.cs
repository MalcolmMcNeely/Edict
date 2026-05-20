using System.Reflection;

using Edict.Contracts.ClaimCheck;

namespace Edict.Contracts.Tests.ClaimCheck;

public sealed class ClaimCheckStoreTests
{
    [Fact]
    public void IEdictClaimCheckStore_ShouldExposeOnlyPutAsyncAndGetAsync()
    {
        var members = typeof(IEdictClaimCheckStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "GetAsync", "PutAsync" }, members);
    }
}
