using Testcontainers.ServiceBus;
using Xunit;

namespace Modulus.Messaging.AzureServiceBus.IntegrationTests.Fixtures;

/// <summary>
/// One Service Bus emulator container (with its required SQL Server companion, managed
/// automatically by the Testcontainers module) shared by all tests in the collection.
/// The emulator does not support <c>ServiceBusAdministrationClient</c>, so topology is
/// pre-declared in the checked-in <c>Config.json</c> and every test runs with
/// <c>MessagingOptions.AutoProvision = false</c>.
/// </summary>
public sealed class ServiceBusEmulatorFixture : IAsyncLifetime
{
    // Pinned explicitly per the Testcontainers.ServiceBus migration guidance: the parameterless
    // constructor and the ServiceBusImage constant are both obsolete as of 4.13.0.
    // https://github.com/testcontainers/testcontainers-dotnet/discussions/1470
    private const string EmulatorImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";

    private readonly ServiceBusContainer _container = new ServiceBusBuilder(EmulatorImage)
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "Config.json"))
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ServiceBusCollection : ICollectionFixture<ServiceBusEmulatorFixture>
{
    public const string Name = "AzureServiceBusEmulator";
}
