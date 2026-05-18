using Xunit;

namespace Sample.Api.Tests;

[CollectionDefinition(Name)]
public sealed class ApiClusterCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "ApiCluster";
}
