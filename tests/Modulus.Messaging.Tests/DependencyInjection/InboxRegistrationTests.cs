using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Inbox;
using Modulus.Messaging.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.DependencyInjection;

public class InboxRegistrationTests
{
    [Fact]
    public void AddModulusInbox_RegistersInboxStoreAndDbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(o =>
        {
            o.Transport = Transport.InMemory;
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });
        services.AddModulusInbox(o => o.UseInMemoryDatabase("inbox-reg-test"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IInboxStore>().ShouldBeOfType<EfInboxStore>();
        scope.ServiceProvider.GetService<InboxDbContext>().ShouldNotBeNull();
    }

    [Fact]
    public void AddModulusMessaging_WithoutAddModulusInbox_DoesNotRegisterInboxStore()
    {
        // Documents the opt-in contract: without AddModulusInbox, IInboxStore is unregistered
        // and IdempotentConsumerAdapter falls through to direct handler execution.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(o =>
        {
            o.Transport = Transport.InMemory;
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IInboxStore>().ShouldBeNull();
    }

    [Fact]
    public void AddModulusOutbox_RegistersDbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusMessaging(o =>
        {
            o.Transport = Transport.InMemory;
            o.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });
        services.AddModulusOutbox(o => o.UseInMemoryDatabase("outbox-reg-test"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<Modulus.Messaging.Outbox.OutboxDbContext>().ShouldNotBeNull();
    }
}
