using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddModulusMediator_registers_IMediator_as_scoped()
    {
        var services = new ServiceCollection();

        services.AddModulusMediator(typeof(ServiceCollectionExtensionsTests).Assembly);

        var descriptor = services.First(d => d.ServiceType == typeof(IMediator));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddModulusMediator_resolves_working_mediator()
    {
        var services = new ServiceCollection();
        services.AddModulusMediator(typeof(ServiceCollectionExtensionsTests).Assembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var mediator = scope.ServiceProvider.GetService<IMediator>();

        mediator.ShouldNotBeNull();
    }
}
