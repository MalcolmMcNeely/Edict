using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// The single chokepoint every route-key emit site folds through, plus its inverse.
// A public aggregate composes to the bare stringified route Guid; a tenant-scoped
// aggregate composes to "{tenant}|{guid}". Parse recovers the exact tenant and route
// Guid or throws rather than mis-parse, so the call filter and forensics can answer
// "which tenant owns this row" from the key alone. The round-trip is the isolation
// guarantee, so it is proven over generated inputs that gate the build.
public sealed class EdictKeyComposerTests
{
    [Fact]
    public void Compose_ShouldReturnBareRouteKey_WhenNoTenant()
    {
        var composed = EdictKeyComposer.Compose(null, "9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e");

        Assert.Equal("9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e", composed);
    }

    [Fact]
    public void Compose_ShouldFoldTenantBeforeRouteKey_WhenTenantPresent()
    {
        var composed = EdictKeyComposer.Compose(EdictTenantId.Of("acme-corp"), "9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e");

        Assert.Equal("acme-corp|9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e", composed);
    }

    [Fact]
    public void Parse_ShouldRecoverNoTenant_WhenKeyIsBareGuid()
    {
        var routeGuid = Guid.NewGuid();

        var parsed = EdictKeyComposer.Parse(routeGuid.ToString("N"));

        Assert.Null(parsed.Tenant);
        Assert.Equal(routeGuid, parsed.RouteKey);
    }

    [Fact]
    public void Parse_ShouldRecoverTenantAndRouteGuid_WhenKeyIsFolded()
    {
        var routeGuid = Guid.NewGuid();

        var parsed = EdictKeyComposer.Parse($"globex|{routeGuid:N}");

        Assert.Equal(EdictTenantId.Of("globex"), parsed.Tenant);
        Assert.Equal(routeGuid, parsed.RouteKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("acme|not-a-guid")]
    [InlineData("acme|")]
    [InlineData("|9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e")]
    [InlineData("acme|globex|9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e")]
    public void Parse_ShouldThrow_WhenKeyIsMalformed(string malformed)
    {
        Assert.Throws<EdictMalformedRoutedKeyException>(() => EdictKeyComposer.Parse(malformed));
    }

    // Property: Parse(Compose(tenant, guid)) recovers the exact pair for every valid
    // input, on both axes. Generated deterministically off a fixed seed so the proof
    // is reproducible and gates the build the same way conformance does. The tenant
    // strings cover the full safe-everywhere charset, including the three separators
    // that a naive split on the wrong character would trip over.
    [Fact]
    public void Parse_ShouldRoundTripCompose_ForGeneratedInputs()
    {
        var random = new Random(0x_5EED);

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            var routeGuid = NewGuid(random);

            // Public axis: no tenant, bare key.
            var bare = EdictKeyComposer.Compose(null, routeGuid.ToString("N"));
            var parsedBare = EdictKeyComposer.Parse(bare);
            Assert.Null(parsedBare.Tenant);
            Assert.Equal(routeGuid, parsedBare.RouteKey);

            // Tenant-scoped axis: folded key recovers the exact tenant and Guid.
            var tenant = EdictTenantId.Of(NewTenantValue(random));
            var folded = EdictKeyComposer.Compose(tenant, routeGuid.ToString("N"));
            var parsedFolded = EdictKeyComposer.Parse(folded);
            Assert.Equal(tenant, parsedFolded.Tenant);
            Assert.Equal(routeGuid, parsedFolded.RouteKey);
        }
    }

    static Guid NewGuid(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    static string NewTenantValue(Random random)
    {
        const string charset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";
        var length = random.Next(1, 40);
        return string.Create(length, random, static (span, sourceRandom) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = charset[sourceRandom.Next(charset.Length)];
            }
        });
    }
}
