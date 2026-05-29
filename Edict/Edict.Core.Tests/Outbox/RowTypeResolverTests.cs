using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Edict.Core.Tests.Outbox;

public sealed class RowTypeResolverTests
{
    static readonly TypeConverter Converter = BuildTypeConverter();

    [Fact]
    public void Resolve_ShouldReturnConcreteType_WhenAliasKnown()
    {
        var resolver = new RowTypeResolver(Converter);

        var type = resolver.Resolve(Converter.Format(typeof(KnownAliasedRow)));

        Assert.Same(typeof(KnownAliasedRow), type);
    }

    [Fact]
    public void Resolve_ShouldThrowEdictUnregisteredTypeException_WhenAliasUnknown()
    {
        var resolver = new RowTypeResolver(Converter);
        const string alias = "Edict.Tests.NoSuchRowAliasShouldNotResolve";

        var exception = Assert.Throws<EdictUnregisteredTypeException>(() => resolver.Resolve(alias));

        Assert.Equal(EdictUnregisteredTypeException.Kind.RowAlias, exception.UnregisteredKind);
        Assert.Equal(alias, exception.TypeName);
    }

    [Fact]
    public void Resolve_ShouldReturnSameCachedTypeReference_OnRepeatedResolution()
    {
        var resolver = new RowTypeResolver(Converter);
        var alias = Converter.Format(typeof(KnownAliasedRow));

        var first = resolver.Resolve(alias);
        var second = resolver.Resolve(alias);

        Assert.Same(first, second);
    }

    static TypeConverter BuildTypeConverter()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b => b.AddAssembly(typeof(RowTypeResolverTests).Assembly));
        return services.BuildServiceProvider().GetRequiredService<TypeConverter>();
    }
}

[GenerateSerializer]
[Alias("Edict.Tests.RowTypeResolverTests.KnownAliasedRow")]
public sealed record KnownAliasedRow
{
    [Id(0)]
    public string Value { get; init; } = "";
}
