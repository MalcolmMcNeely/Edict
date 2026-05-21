using Edict.Core;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Core.Tests.Configuration;

public sealed class ValidatorAutoDiscoveryTests
{
    [Fact]
    public void AddEdict_ShouldResolveConsumerValidator_FromSuppliedAssembly()
    {
        var services = new ServiceCollection();

        services.AddEdict(typeof(ValidatorAutoDiscoveryTests).Assembly);

        using var sp = services.BuildServiceProvider();
        var validator = sp.GetService<IValidator<ValidateSkuCommand>>();

        Assert.IsType<ValidateSkuConsumerValidator>(validator);
    }
}

public sealed class ValidateSkuConsumerValidator : AbstractValidator<ValidateSkuCommand>
{
    public ValidateSkuConsumerValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
    }
}
