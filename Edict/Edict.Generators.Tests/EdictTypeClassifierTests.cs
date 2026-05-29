using System.Collections.Immutable;

using Edict.Generators.Classification;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Tests;

public class EdictTypeClassifierTests
{
    [Fact]
    public void Classify_ReturnsCommand_ForPartialRecordDerivingFromEdictCommand()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;

            namespace Sample;

            public sealed partial record PlaceOrder(Guid Id) : EdictCommand
            {
                [EdictRouteKey]
                public Guid Id { get; init; } = Id;
            }
            """;

        Assert.Equal(EdictTypeKind.Command, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsEvent_ForPartialRecordDerivingFromEdictEvent()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;

            namespace Sample;

            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid Id) : EdictEvent
            {
                [EdictRouteKey]
                public Guid Id { get; init; } = Id;
            }
            """;

        Assert.Equal(EdictTypeKind.Event, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsCommandHandler_ForPartialClassDerivingFromEdictCommandHandler()
    {
        const string source = """
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;

            namespace Sample;

            public partial class OrderCommandHandler : EdictCommandHandler
            {
            }
            """;

        Assert.Equal(EdictTypeKind.CommandHandler, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsCommandHandler_ForPartialClassDerivingFromGenericStatefulBase()
    {
        const string source = """
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;

            namespace Sample;

            public sealed class OrderState { public string Status { get; set; } = ""; }

            public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
            {
            }
            """;

        Assert.Equal(EdictTypeKind.CommandHandler, ClassifyFirstTypeDeclaration(source, skip: 1));
    }

    [Fact]
    public void Classify_ReturnsEventHandler_ForPartialClassDerivingFromEdictEventHandler()
    {
        const string source = """
            using Edict.Core.EventHandler;

            namespace Sample;

            public sealed partial class OrderEmailHandler : EdictEventHandler
            {
            }
            """;

        Assert.Equal(EdictTypeKind.EventHandler, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsProjectionBuilder_ForPartialClassDerivingFromEdictProjectionBuilder()
    {
        const string source = """
            using Edict.Core.Projections;

            namespace Sample;

            public sealed partial class OrderProjectionBuilder : EdictProjectionBuilder
            {
            }
            """;

        Assert.Equal(EdictTypeKind.ProjectionBuilder, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsSaga_ForPartialClassDerivingFromGenericEdictSaga()
    {
        const string source = """
            using Edict.Core.Sagas;

            namespace Sample;

            public sealed class OrderSagaProgress { public bool Placed { get; set; } }

            public sealed partial class OrderSaga : EdictSaga<OrderSagaProgress>
            {
            }
            """;

        Assert.Equal(EdictTypeKind.Saga, ClassifyFirstTypeDeclaration(source, skip: 1));
    }

    [Fact]
    public void Classify_ReturnsNone_ForRecordWithoutPartialModifier()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;

            namespace Sample;

            public sealed record PlaceOrder(Guid Id) : EdictCommand
            {
                [EdictRouteKey]
                public Guid Id { get; init; } = Id;
            }
            """;

        Assert.Equal(EdictTypeKind.None, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsNone_ForClassWithoutPartialModifier()
    {
        const string source = """
            using Edict.Core.EventHandler;

            namespace Sample;

            public sealed class OrderEmailHandler : EdictEventHandler
            {
            }
            """;

        Assert.Equal(EdictTypeKind.None, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsNone_ForPartialRecordWithoutBaseList()
    {
        const string source = """
            namespace Sample;

            public sealed partial record PlaceOrder(System.Guid Id);
            """;

        Assert.Equal(EdictTypeKind.None, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsNone_ForFileLocalPartialRecord()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;

            namespace Sample;

            file partial record OrderPlacedEvent(Guid Id) : EdictEvent
            {
                [EdictRouteKey]
                public Guid Id { get; init; } = Id;
            }
            """;

        Assert.Equal(EdictTypeKind.None, ClassifyFirstTypeDeclaration(source));
    }

    [Fact]
    public void Classify_ReturnsNone_ForPrivateNestedPartialRecord()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;

            namespace Sample;

            public sealed class Outer
            {
                sealed partial record OrderPlacedEvent(Guid Id) : EdictEvent
                {
                    [EdictRouteKey]
                    public Guid Id { get; init; } = Id;
                }
            }
            """;

        Assert.Equal(EdictTypeKind.None, ClassifyFirstTypeDeclaration(source, skip: 1));
    }

    [Fact]
    public void Classify_ReturnsNone_ForUnrelatedType()
    {
        const string source = """
            namespace Sample;

            public sealed partial class Unrelated : System.Exception
            {
            }
            """;

        Assert.Equal(EdictTypeKind.None, ClassifyFirstTypeDeclaration(source));
    }

    static EdictTypeKind ClassifyFirstTypeDeclaration(string source, int skip = 0)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            assemblyName: "ClassifierUnderTest",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);

        var node = tree.GetRoot().DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Skip(skip)
            .First();

        return EdictTypeClassifier.Classify(node, model);
    }
}
