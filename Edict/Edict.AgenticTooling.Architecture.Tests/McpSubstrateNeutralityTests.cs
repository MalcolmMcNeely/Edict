using System.Reflection;

using Edict.Mcp;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class McpSubstrateNeutralityTests
{
    static readonly Assembly McpAssembly = typeof(EdictMcpServer).Assembly;

    [Fact]
    public void EdictMcp_HasNoAzureReferences()
    {
        var violations = ReferencedAssemblyNames()
            .Where(name => name.StartsWith("Edict.Azure", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void EdictMcp_HasNoPostgresReference()
    {
        var violations = ReferencedAssemblyNames()
            .Where(name => name.Equals("Edict.Postgres", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void EdictMcp_HasNoKafkaReference()
    {
        var violations = ReferencedAssemblyNames()
            .Where(name => name.Equals("Edict.Kafka", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void EdictMcp_HasNoSubstrateReferences()
    {
        var violations = ReferencedAssemblyNames()
            .Where(name => name.StartsWith("Edict.Substrate", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void EdictMcp_HasNoEdictTestingReference()
    {
        var violations = ReferencedAssemblyNames()
            .Where(name => name.Equals("Edict.Testing", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(violations);
    }

    static IEnumerable<string> ReferencedAssemblyNames()
    {
        return McpAssembly
            .GetReferencedAssemblies()
            .Select(referenced => referenced.Name)
            .Where(name => name is not null)!;
    }
}
