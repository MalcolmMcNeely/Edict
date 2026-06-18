using Edict.Contracts.Tenancy;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// EdictTenantId.Of is the delimiter-injection control: a tenant id is folded into
// routed grain and stream keys, so the charset is a security boundary. These cover
// each named hazardous set (key delimiter, claim-check path separator,
// storage-reserved, Postgres-unsafe, whitespace/control/non-ASCII) and the safe
// set's round-trip. Single-fact assertions, so targeted Assert over a Verify.
public sealed class EdictTenantIdCharsetTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("Acme-Corp")]
    [InlineData("tenant_42")]
    [InlineData("a.b.c")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("ABCdef123._-")]
    public void Of_WithSafeValue_RoundTripsTheValue(string value)
    {
        Assert.Equal(value, EdictTenantId.Of(value).Value);
    }

    [Theory]
    [InlineData("acme|globex")]   // the composed-key delimiter — the forging vector
    [InlineData("acme/globex")]   // the claim-check blob-path separator
    [InlineData("acme\\globex")]  // backslash, Azure-Table-reserved
    [InlineData("acme#corp")]     // Azure-Table-reserved
    [InlineData("acme?corp")]     // Azure-Table-reserved
    [InlineData("acme:corp")]     // outside the safe set
    [InlineData("acme corp")]     // whitespace
    [InlineData("acme\tcorp")]    // control character
    [InlineData("acme\ncorp")]    // control character
    [InlineData("acme';drop")]    // Postgres-unsafe punctuation
    [InlineData("acmé")]          // non-ASCII
    [InlineData("")]              // empty — a tenant with no value is meaningless
    public void Of_WithUnsafeValue_Throws(string value)
    {
        Assert.Throws<EdictInvalidTenantIdException>(() => EdictTenantId.Of(value));
    }

    [Fact]
    public void Of_WithNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EdictTenantId.Of(null!));
    }

    // Direct construction is its own path: validation lives in the constructor so
    // no caller — Of, a consumer, or the serializer — can mint an unsafe value.
    [Theory]
    [InlineData("acme|globex")]
    [InlineData("acme corp")]
    [InlineData("")]
    public void Constructor_WithUnsafeValue_Throws(string value)
    {
        Assert.Throws<EdictInvalidTenantIdException>(() => new EdictTenantId(value));
    }
}
