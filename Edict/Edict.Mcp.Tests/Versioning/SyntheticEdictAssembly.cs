using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Edict.Mcp.Tests.Versioning;

static class SyntheticEdictAssembly
{
    public static string Emit(string directory, string assemblyName, string informationalVersion)
    {
        var source = $"""
            [assembly: System.Reflection.AssemblyInformationalVersionAttribute("{informationalVersion}")]
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var coreLibraryReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [syntaxTree],
            references: [coreLibraryReference],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var dllPath = Path.Combine(directory, assemblyName + ".dll");
        using var fileStream = File.Create(dllPath);
        EmitResult emitResult = compilation.Emit(fileStream);
        if (!emitResult.Success)
        {
            var diagnostics = string.Join(Environment.NewLine, emitResult.Diagnostics);
            throw new InvalidOperationException(
                $"Failed to emit synthetic assembly '{assemblyName}': {diagnostics}");
        }
        return dllPath;
    }
}
