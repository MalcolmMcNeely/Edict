using System.Text.Json;

using Edict.Generators;

namespace Edict.Generators.Tests;

/// <summary>
/// Pins the snake-case algorithm used to derive <c>edict.{snake_case_prop}</c>
/// tag keys. The worked-examples table is the public convention
/// contract; both <see cref="JsonNamingPolicy.SnakeCaseLower"/> and the
/// in-house netstandard2.0 port (<see cref="SnakeCaseLower"/>) must produce it.
/// </summary>
public class TelemeterizedTagNamingTests
{
    [Theory]
    [InlineData("Sku", "sku")]
    [InlineData("OrderId", "order_id")]
    [InlineData("CustomerID", "customer_id")]
    [InlineData("SKU", "sku")]
    [InlineData("HTTPMethod", "http_method")]
    [InlineData("URL", "url")]
    public void BclSnakeCaseLower_MatchesWorkedExamples(string propertyName, string expected)
    {
        Assert.Equal(expected, JsonNamingPolicy.SnakeCaseLower.ConvertName(propertyName));
    }

    [Theory]
    [InlineData("Sku", "sku")]
    [InlineData("OrderId", "order_id")]
    [InlineData("CustomerID", "customer_id")]
    [InlineData("SKU", "sku")]
    [InlineData("HTTPMethod", "http_method")]
    [InlineData("URL", "url")]
    public void GeneratorSnakeCaseLower_MatchesWorkedExamples(string propertyName, string expected)
    {
        Assert.Equal(expected, SnakeCaseLower.Convert(propertyName));
    }
}
