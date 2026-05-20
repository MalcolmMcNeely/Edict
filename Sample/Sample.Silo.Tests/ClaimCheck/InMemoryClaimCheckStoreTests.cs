using Edict.Testing.ClaimCheck;

namespace Sample.Silo.Tests.ClaimCheck;

/// <summary>
/// Conformance for the shipped in-memory <see cref="InMemoryClaimCheckStore"/>
/// (ADR 0024). Lives in the Sample test project because that is the only
/// suite with a project reference to <c>Edict.Testing</c>. Mirrors the
/// publisher-side seam contract — <c>PutAsync</c> round-trips via
/// <c>GetAsync</c>; a missing-blob fetch throws a recognisable exception
/// (slice 3 will funnel that into the dead-letter promotion path).
/// </summary>
public sealed class InMemoryClaimCheckStoreTests
{
    [Fact]
    public async Task PutAsync_ShouldReturnKeyThatRoundTripsViaGetAsync()
    {
        var store = new InMemoryClaimCheckStore();
        byte[] payload = [1, 2, 3, 4, 5];

        var key = await store.PutAsync(payload, CancellationToken.None);
        var fetched = await store.GetAsync(key, CancellationToken.None);

        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task GetAsync_ShouldThrow_WhenKeyUnknown()
    {
        var store = new InMemoryClaimCheckStore();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => store.GetAsync("missing-key", CancellationToken.None));
    }

    [Fact]
    public async Task PutAsync_ShouldReturnDistinctKeysForDistinctPayloads()
    {
        var store = new InMemoryClaimCheckStore();

        var k1 = await store.PutAsync(new byte[] { 1 }, CancellationToken.None);
        var k2 = await store.PutAsync(new byte[] { 2 }, CancellationToken.None);

        Assert.NotEqual(k1, k2);
    }
}
