# Integration Testing

Integration tests verify that your module works correctly when all the real pieces are wired together -- endpoints, handlers, database, and messaging. The scaffolded `Tests.Integration` project uses `WebApplicationFactory` to host the full application in-process and provides a test base class for clean, isolated test execution.

## WebApplicationFactory Setup

The generated integration test project includes a custom `WebApplicationFactory<T>` that configures the application for testing:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace EShop.Modules.Catalog.Tests.Integration;

public class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("eshop_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the production database registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            // Register test database using Testcontainers
            services.AddDbContext<CatalogDbContext>(options =>
                options.UseNpgsql(_dbContainer.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

::: info Testcontainers requires Docker
Integration tests use [Testcontainers](https://dotnet.testcontainers.org/) to spin up a real PostgreSQL instance in Docker. Ensure Docker Desktop (or a compatible runtime) is running before executing integration tests.
:::

## Test Base Class

The test base class provides shared setup for all integration tests in a module. It creates the `HttpClient`, applies database migrations, and optionally resets the database between tests:

```csharp
namespace EShop.Modules.Catalog.Tests.Integration;

[Collection("Catalog")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly HttpClient Client;
    protected readonly CatalogApiFactory Factory;

    protected IntegrationTestBase(CatalogApiFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Apply migrations before each test class
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        // Clean up the database after each test class
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
    }
}
```

### Collection Fixture

Use xUnit collection fixtures to share the `WebApplicationFactory` (and Docker container) across all tests in a module:

```csharp
[CollectionDefinition("Catalog")]
public class CatalogCollectionFixture : ICollectionFixture<CatalogApiFactory>;
```

This ensures the PostgreSQL container starts once per test run, not once per test class. Each test class still gets a fresh database via `EnsureCreatedAsync` / `EnsureDeletedAsync` in the base class.

## Testing Endpoints End-to-End

### POST and GET Roundtrip

The most common integration test pattern: create an entity via POST, then retrieve it via GET and verify the response.

```csharp
using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace EShop.Modules.Catalog.Tests.Integration.Products;

[Collection("Catalog")]
public class ProductEndpointTests : IntegrationTestBase
{
    public ProductEndpointTests(CatalogApiFactory factory) : base(factory) { }

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
        var response = await Client.PostAsJsonAsync("/catalog", request);

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
        var createResponse = await Client.PostAsJsonAsync("/catalog", createRequest);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Act -- Get
        var getResponse = await Client.GetAsync($"/catalog/{id}");

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
        var response = await Client.GetAsync($"/catalog/{Guid.NewGuid()}");

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
        var response = await Client.PostAsJsonAsync("/catalog", request);

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
    var createResponse = await Client.PostAsJsonAsync("/catalog", createRequest);
    var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

    // Act
    var deleteResponse = await Client.DeleteAsync($"/catalog/{id}");

    // Assert
    deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

    // Verify the product is gone
    var getResponse = await Client.GetAsync($"/catalog/{id}");
    getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
}
```

## In-Memory Database Alternative

For faster tests that do not require PostgreSQL-specific features, you can use the EF Core in-memory provider:

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
The EF Core in-memory provider does not support transactions, raw SQL, database-specific features, or referential integrity constraints. Use it for quick smoke tests, but rely on Testcontainers with a real PostgreSQL instance for comprehensive integration testing.
:::

## Testing with InMemory Messaging

When your module publishes or consumes integration events, configure the InMemory transport for tests so messages are dispatched without a real broker:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        // Replace the production message bus with InMemory
        services.AddModulusMessaging(config =>
        {
            config.UseInMemoryTransport();
        });
    });
}
```

You can then verify that integration events were published by consuming them in the test:

```csharp
[Fact]
public async Task CreateProduct_PublishesCatalogItemCreatedEvent()
{
    // Arrange
    var harness = Factory.Services.GetRequiredService<ITestHarness>();
    await harness.Start();

    var request = new { Name = "Widget", Price = 9.99m, Sku = "WDG-EVT" };

    // Act
    await Client.PostAsJsonAsync("/catalog", request);

    // Assert
    (await harness.Published
        .Any<CatalogItemCreatedEvent>())
        .ShouldBeTrue();

    await harness.Stop();
}
```

::: info MassTransit Test Harness
The `ITestHarness` interface comes from the MassTransit testing package. It intercepts all published and consumed messages, making it straightforward to verify messaging behavior without a real broker.
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
    var response = await Client.PostAsJsonAsync("/catalog", request);
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

- **Use Testcontainers for database tests.** A real PostgreSQL instance catches issues that in-memory providers miss (e.g., migration errors, constraint violations, query translation differences).
- **Share the factory across tests.** Use xUnit collection fixtures to start Docker containers once per test run, not once per test class.
- **Reset state between tests.** Use `EnsureDeletedAsync` / `EnsureCreatedAsync` or a database cleanup strategy to ensure tests do not leak state.
- **Test the HTTP contract.** Assert status codes, response headers (`Location` for 201), and response bodies. Integration tests verify the full request/response cycle.
- **Keep integration tests focused.** Test the API contract and data persistence. Do not re-test business logic that is already covered by unit tests.
- **Run integration tests separately in CI.** They require Docker and are slower than unit tests. Use test filters to separate them: `dotnet test --filter "FullyQualifiedName~Tests.Integration"`.

## See Also

- [Unit Testing](./unit-testing) -- Test handlers and domain logic in isolation
- [Architecture Tests](./architecture-tests) -- Enforce layer dependency rules
- [Messaging: Transports](/messaging/transports) -- InMemory transport for testing
