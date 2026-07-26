# Integration Testing

Integration tests verify that your module works correctly when all the real pieces are wired together -- endpoints, handlers, database, and messaging. The scaffolded `Tests.Integration` project uses `WebApplicationFactory` to host the full application in-process and provides a test base class for clean, isolated test execution.

## The Scaffolded Test Base Class

`modulus add-module Catalog` generates `CatalogIntegrationTestBase`: it starts a SQL Server container with Testcontainers, hosts the app with `WebApplicationFactory<Program>`, and points the app at the container by overriding the `ConnectionStrings:Default` configuration value -- no service-collection surgery needed, because module DbContexts read their connection string from configuration:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace EShop.Catalog.Tests.Integration;

public abstract class CatalogIntegrationTestBase : IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    protected WebApplicationFactory<Program> Factory = null!;
    protected HttpClient Client = null!;

    public virtual async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _dbContainer.GetConnectionString()
                    });
                });
            });

        Client = Factory.CreateClient();
    }

    public virtual async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }
}
```

Alongside it, the scaffold generates a starter `CatalogEndpointTests` class that makes a first HTTP request against the module's `/api/catalog` surface. The `Tests.Integration` project references the module's four source projects plus the WebApi host (for `Program`).

::: info Testcontainers requires Docker
Integration tests use [Testcontainers](https://dotnet.testcontainers.org/) to spin up a real SQL Server instance in Docker. Ensure Docker Desktop (or a compatible runtime) is running before executing integration tests.
:::

### Optional: Share the Container with a Collection Fixture

The scaffolded base class starts one container per test class (via `IAsyncLifetime`). As your suite grows, promote the factory to an xUnit collection fixture so the container starts once per test run:

```csharp
[CollectionDefinition("Catalog")]
public class CatalogCollectionFixture : ICollectionFixture<CatalogApiFactory>;
```

where `CatalogApiFactory` is a `WebApplicationFactory<Program>` + `IAsyncLifetime` class you extract from the base class above. Each test class can then reset state between runs with `EnsureDeletedAsync` / `EnsureCreatedAsync` on the module `DbContext`.

## Testing Endpoints End-to-End

### POST and GET Roundtrip

The most common integration test pattern: create an entity via POST, then retrieve it via GET and verify the response.

```csharp
using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace EShop.Catalog.Tests.Integration;

public class ProductEndpointTests : CatalogIntegrationTestBase
{
    [Fact]
    public async Task CreateProduct_ValidRequest_Returns201WithId()
    {
        // Arrange
        var request = new
        {
            Name = "Widget",
            Price = 9.99m,
            Sku = "WDG-001"
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/catalog", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var id = await response.Content.ReadFromJsonAsync<Guid>();
        id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateAndGetProduct_Roundtrip_ReturnsCorrectData()
    {
        // Arrange
        var createRequest = new
        {
            Name = "Widget",
            Price = 9.99m,
            Sku = "WDG-002"
        };

        // Act -- Create
        var createResponse = await Client.PostAsJsonAsync("/api/catalog", createRequest);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Act -- Get
        var getResponse = await Client.GetAsync($"/api/catalog/{id}");

        // Assert
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var product = await getResponse.Content.ReadFromJsonAsync<ProductDto>();
        product.ShouldNotBeNull();
        product.Name.ShouldBe("Widget");
        product.Price.ShouldBe(9.99m);
    }

    [Fact]
    public async Task GetProduct_NonExistentId_Returns404()
    {
        // Act
        var response = await Client.GetAsync($"/api/catalog/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateProduct_InvalidRequest_Returns400()
    {
        // Arrange
        var request = new
        {
            Name = "",          // empty -- will fail validation
            Price = -1.00m,     // negative -- will fail validation
            Sku = "WDG-003"
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/catalog", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
```

### Testing DELETE

```csharp
[Fact]
public async Task DeleteProduct_ExistingProduct_Returns204()
{
    // Arrange -- create a product first
    var createRequest = new { Name = "Widget", Price = 9.99m, Sku = "WDG-DEL" };
    var createResponse = await Client.PostAsJsonAsync("/api/catalog", createRequest);
    var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

    // Act
    var deleteResponse = await Client.DeleteAsync($"/api/catalog/{id}");

    // Assert
    deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

    // Verify the product is gone
    var getResponse = await Client.GetAsync($"/api/catalog/{id}");
    getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
}
```

## In-Memory Database Alternative

For faster tests that do not require SQL Server-specific features, you can use the EF Core in-memory provider:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>));

        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseInMemoryDatabase($"CatalogTest_{Guid.NewGuid()}"));
    });
}
```

::: warning In-memory limitations
The EF Core in-memory provider does not support transactions, raw SQL, database-specific features, or referential integrity constraints. Use it for quick smoke tests, but rely on Testcontainers with a real SQL Server instance for comprehensive integration testing.
:::

## Testing with InMemory Messaging

When your module publishes or consumes integration events, select the InMemory transport for tests so messages are dispatched without a real broker. Because the transport is chosen by the `Messaging` configuration section, the test factory only needs to override one setting:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    // Config-driven transport switching: force InMemory for tests
    builder.UseSetting("Messaging:Transport", "InMemory");
}
```

Handlers, publishers, and the outbox/inbox pipeline all run exactly as in production -- only the broker is replaced. Delivery on the InMemory transport is immediate, so no fixed delays are needed in tests.

To verify that an integration event was published, assert against the outbox table -- an outbox row is the durable record that the event will be (or was) dispatched. How atomic that row is with the business data depends on your outbox configuration: with the outbox [mapped into the module's DbContext](/messaging/outbox-pattern#transactionality-the-two-configurations) it commits in the same transaction; with the default standalone store it commits separately.

```csharp
[Fact]
public async Task CreateProduct_WritesCatalogItemCreatedEventToOutbox()
{
    // Arrange
    var request = new { Name = "Widget", Price = 9.99m, Sku = "WDG-EVT" };

    // Act
    await Client.PostAsJsonAsync("/api/catalog", request);

    // Assert -- an outbox row exists for the event
    using var scope = Factory.Services.CreateScope();
    var outbox = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

    (await outbox.OutboxMessages
        .AnyAsync(m => m.EventType.Contains(nameof(CatalogItemCreatedEvent))))
        .ShouldBeTrue();
}
```

To verify end-to-end consumption, register a recording `IIntegrationEventHandler<T>` in an assembly included in `MessagingOptions.Assemblies` and assert it was invoked.

::: tip Prefer ModulusKit.Testing over hand-written queries
The `outbox.OutboxMessages.AnyAsync(...)` query above is the raw version of what
[`ModulusKit.Testing`](./modulus-testing)'s `OutboxTestQueries` gives you as a one-liner
(`await Factory.Services.GetOutboxMessagesAsync()`), and its `ModulusMessagingTestHarness` +
`TestMessageTransport` replace hand-rolling a fake `IMessageTransport` for module-level messaging
tests that don't need a full `WebApplicationFactory`. See [ModulusKit.Testing](./modulus-testing)
for the full reference.
:::

::: info Broker-level integration tests
The InMemory transport covers the consumer pipeline (deserialization, inbox idempotency, retry) but not broker topology. For tests against a real broker, spin up RabbitMQ with Testcontainers -- this is how the Modulus library itself tests its RabbitMQ transport (tests marked `Category=Integration`).
:::

## Accessing Services in Tests

Sometimes you need to resolve services from the DI container to set up or verify test state:

```csharp
[Fact]
public async Task CreateProduct_PersistsToDatabase()
{
    // Arrange
    var request = new { Name = "Widget", Price = 9.99m, Sku = "WDG-DB" };

    // Act
    var response = await Client.PostAsJsonAsync("/api/catalog", request);
    var id = await response.Content.ReadFromJsonAsync<Guid>();

    // Assert -- verify directly in the database
    using var scope = Factory.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var product = await dbContext.Products.FindAsync(id);

    product.ShouldNotBeNull();
    product.Name.ShouldBe("Widget");
}
```

## Best Practices

- **Use Testcontainers for database tests.** A real SQL Server instance catches issues that in-memory providers miss (e.g., migration errors, constraint violations, query translation differences).
- **Share the factory across tests.** Use xUnit collection fixtures to start Docker containers once per test run, not once per test class.
- **Reset state between tests.** Use `EnsureDeletedAsync` / `EnsureCreatedAsync` or a database cleanup strategy to ensure tests do not leak state.
- **Test the HTTP contract.** Assert status codes, response headers (`Location` for 201), and response bodies. Integration tests verify the full request/response cycle.
- **Keep integration tests focused.** Test the API contract and data persistence. Do not re-test business logic that is already covered by unit tests.
- **Run integration tests separately in CI.** They require Docker and are slower than unit tests. Use test filters to separate them: `dotnet test --filter "FullyQualifiedName~Tests.Integration"`.

## See Also

- [Unit Testing](./unit-testing) -- Test handlers and domain logic in isolation
- [Architecture Tests](./architecture-tests) -- Enforce layer dependency rules
- [Messaging: Transports](/messaging/transports) -- InMemory transport for testing
