using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Edict.Architecture.Tests;

// The tenant fold lands behind one Compose chokepoint, but the route key reaches a
// key at separate generator emit sites — the command grain key and the stream key
// (which doubles as the implicitly-subscribed projection/saga grain key). This
// guard runs the generator over a tenant-scoped aggregate and fails if either
// registrar ever stringifies its route key without folding it through Compose: the
// exact bypass that would route tenant data into the default key space.
public sealed class TenantScopedCompositionCoverageTests
{
    const string TenantScopedAggregate = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Events;
        using Edict.Contracts.Tenancy;
        using Edict.Core.Commands;

        namespace Sample;

        [EdictTenantScoped]
        public readonly record struct EmployeeId(Guid Value);

        public sealed partial record AddEmployee([property: EdictRouteKey] EmployeeId EmployeeId) : EdictCommand;

        [EdictStream("Employees")]
        public sealed partial record EmployeeAdded([property: EdictRouteKey] EmployeeId EmployeeId) : EdictEvent;

        public partial class EmployeeCommandHandler : EdictCommandHandler
        {
            Task<EdictCommandResult> HandleAsync(AddEmployee command)
            {
                Raise(new EmployeeAdded(command.EmployeeId));
                return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
        }
        """;

    [Theory]
    [InlineData("EdictRouteRegistrar.g.cs")]
    [InlineData("EdictEventStreamRegistrar.g.cs")]
    public void EveryTenantScopedRouteKeyStringification_FoldsThroughCompose(string registrarFileSuffix)
    {
        var generated = RunGenerator(TenantScopedAggregate);
        var registrar = generated
            .Single(file => file.Key.EndsWith(registrarFileSuffix, StringComparison.Ordinal))
            .Value;

        // A route key reaches its grain or stream key as the "N"-format string of a
        // Guid; every such stringification in a registrar must sit inside the one
        // composition seam, or a tenant-scoped key has bypassed the wall.
        var stringificationLines = registrar
            .Split('\n')
            .Where(line => line.Contains(".ToString(\"N\")", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(stringificationLines);
        Assert.All(stringificationLines, line =>
            Assert.Contains("global::Edict.Contracts.Routing.EdictKeyComposer.Compose(", line, StringComparison.Ordinal));
    }

    static IReadOnlyDictionary<string, string> RunGenerator(string consumerSource)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var compilation = CSharpCompilation.Create(
            assemblyName: "TenantScopedConsumerUnderTest",
            syntaxTrees: [CSharpSyntaxTree.ParseText(consumerSource.Replace("\r\n", "\n"))],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var ranDriver = CSharpGeneratorDriver
            .Create(new EdictGenerator().AsSourceGenerator())
            .RunGenerators(compilation);

        return ranDriver.GetRunResult().GeneratedTrees
            .ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.GetText().ToString());
    }
}
