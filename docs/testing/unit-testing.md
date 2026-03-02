# Unit Testing

Unit tests verify handlers, validators, and domain logic in isolation. By mocking infrastructure dependencies like repositories and the unit of work, you can test business logic without a database, HTTP server, or message broker.

## Testing Command Handlers

Command handlers contain the core business logic of your application. Test them by mocking the repository and unit of work, then asserting the `Result` outcome.

### Setup Pattern

```csharp
using NSubstitute;
using Shouldly;

namespace EShop.Modules.Catalog.Tests.Unit.Products.Commands;

public class CreateProductHandlerTests
{
    private readonly IRepository<Product> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateProductHandler _sut;

    public CreateProductHandlerTests()
    {
        _repository = Substitute.For<IRepository<Product>>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new CreateProductHandler(_repository, _unitOfWork);
    }
}
```

### Asserting Success

```csharp
[Fact]
public async Task Handle_ValidCommand_ReturnsSuccessWithId()
{
    // Arrange
    var command = new CreateProduct("Widget", 9.99m);

    // Act
    var result = await _sut.Handle(command, CancellationToken.None);

    // Assert
    result.IsSuccess.ShouldBeTrue();
    result.Value.ShouldNotBe(Guid.Empty);
}
```

### Asserting Failure with Specific Error Code

```csharp
[Fact]
public async Task Handle_DuplicateSku_ReturnsConflictError()
{
    // Arrange
    var command = new CreateProduct("Widget", 9.99m);

    _repository.GetBySkuAsync(command.Sku, Arg.Any<CancellationToken>())
        .Returns(new Product("Existing Widget", 5.00m, command.Sku));

    // Act
    var result = await _sut.Handle(command, CancellationToken.None);

    // Assert
    result.IsFailure.ShouldBeTrue();
    result.Errors.ShouldContain(e => e.Code == "Product.DuplicateSku");
    result.Errors[0].Type.ShouldBe(ErrorType.Conflict);
}
```

### Asserting Side Effects

```csharp
[Fact]
public async Task Handle_ValidCommand_AddsProductAndCommits()
{
    // Arrange
    var command = new CreateProduct("Widget", 9.99m);

    // Act
    await _sut.Handle(command, CancellationToken.None);

    // Assert
    await _repository.Received(1).AddAsync(
        Arg.Is<Product>(p => p.Name == "Widget"),
        Arg.Any<CancellationToken>());

    await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
}
```

### Testing Commands That Return No Value

```csharp
[Fact]
public async Task Handle_ExistingProduct_ReturnsSuccess()
{
    // Arrange
    var product = Product.Create("Widget", 9.99m);
    var command = new DeleteProductCommand(product.Id);

    _repository.GetByIdAsync<Guid>(product.Id, Arg.Any<CancellationToken>())
        .Returns(product);

    // Act
    var result = await _sut.Handle(command, CancellationToken.None);

    // Assert
    result.IsSuccess.ShouldBeTrue();
}

[Fact]
public async Task Handle_NonExistentProduct_ReturnsNotFound()
{
    // Arrange
    var command = new DeleteProductCommand(Guid.NewGuid());

    _repository.GetByIdAsync<Guid>(command.ProductId, Arg.Any<CancellationToken>())
        .Returns((Product?)null);

    // Act
    var result = await _sut.Handle(command, CancellationToken.None);

    // Assert
    result.IsFailure.ShouldBeTrue();
    result.Errors.ShouldContain(e => e.Code == "Product.NotFound");
}
```

## Testing Query Handlers

Query handlers retrieve data and return DTOs. Mock the repository or `IQueryDb` interface to supply test data.

```csharp
public class GetProductByIdHandlerTests
{
    private readonly IRepository<Product> _repository;
    private readonly GetProductByIdHandler _sut;

    public GetProductByIdHandlerTests()
    {
        _repository = Substitute.For<IRepository<Product>>();
        _sut = new GetProductByIdHandler(_repository);
    }

    [Fact]
    public async Task Handle_ExistingProduct_ReturnsDtoWithCorrectValues()
    {
        // Arrange
        var product = Product.Create("Widget", 9.99m);
        var query = new GetProductByIdQuery(product.Id);

        _repository.GetByIdAsync<Guid>(product.Id, Arg.Any<CancellationToken>())
            .Returns(product);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Name.ShouldBe("Widget");
        result.Value.Price.ShouldBe(9.99m);
    }

    [Fact]
    public async Task Handle_NonExistentProduct_ReturnsNotFound()
    {
        // Arrange
        var query = new GetProductByIdQuery(Guid.NewGuid());

        _repository.GetByIdAsync<Guid>(query.ProductId, Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("Product.NotFound");
        result.Errors[0].Type.ShouldBe(ErrorType.NotFound);
    }
}
```

## Testing Validators

FluentValidation provides a `TestValidate` extension method that makes validator testing concise. You do not need to mock anything -- validators are pure functions.

```csharp
using FluentValidation.TestHelper;

namespace EShop.Modules.Catalog.Tests.Unit.Products.Validators;

public class CreateProductValidatorTests
{
    private readonly CreateProductValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        // Arrange
        var command = new CreateProduct("Widget", 9.99m);

        // Act
        var result = _sut.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_HasValidationError()
    {
        // Arrange
        var command = new CreateProduct("", 9.99m);

        // Act
        var result = _sut.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NegativePrice_HasValidationError()
    {
        // Arrange
        var command = new CreateProduct("Widget", -1.00m);

        // Act
        var result = _sut.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void Validate_NameExceedsMaxLength_HasValidationError()
    {
        // Arrange
        var command = new CreateProduct(new string('A', 201), 9.99m);

        // Act
        var result = _sut.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("'Name' must be 200 characters or fewer.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_ZeroOrNegativePrice_HasValidationError(decimal price)
    {
        // Arrange
        var command = new CreateProduct("Widget", price);

        // Act
        var result = _sut.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }
}
```

::: tip TestValidate is your friend
The `TestValidate` extension from FluentValidation returns a `TestValidationResult<T>` that supports the `ShouldHaveValidationErrorFor` and `ShouldNotHaveAnyValidationErrors` assertion methods. This is far more readable than calling `Validate` and inspecting the errors manually.
:::

## Testing Domain Events

Domain events are raised by aggregate roots. Test them by invoking the domain method and inspecting the `DomainEvents` collection.

```csharp
namespace EShop.Modules.Catalog.Tests.Unit.Domain;

public class ProductTests
{
    [Fact]
    public void Create_RaisesProductCreatedEvent()
    {
        // Act
        var product = Product.Create("Widget", 9.99m);

        // Assert
        product.DomainEvents.ShouldHaveSingleItem();
        product.DomainEvents[0].ShouldBeOfType<ProductCreatedEvent>();
    }

    [Fact]
    public void Create_ProductCreatedEvent_ContainsCorrectData()
    {
        // Act
        var product = Product.Create("Widget", 9.99m);

        // Assert
        var domainEvent = product.DomainEvents[0].ShouldBeOfType<ProductCreatedEvent>();
        domainEvent.ProductId.ShouldBe(product.Id);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        // Arrange
        var product = Product.Create("Widget", 9.99m);
        product.DomainEvents.Count.ShouldBe(1);

        // Act
        product.ClearDomainEvents();

        // Assert
        product.DomainEvents.ShouldBeEmpty();
    }
}
```

## Testing Domain Logic

Test value objects and entity business rules directly without mocking:

```csharp
public class MoneyTests
{
    [Fact]
    public void Constructor_NegativeAmount_ThrowsDomainException()
    {
        // Act & Assert
        Should.Throw<DomainException>(() => new Money(-1, "USD"));
    }

    [Fact]
    public void Equals_SameAmountAndCurrency_ReturnsTrue()
    {
        // Arrange
        var a = new Money(10.00m, "USD");
        var b = new Money(10.00m, "USD");

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void Equals_DifferentCurrency_ReturnsFalse()
    {
        // Arrange
        var usd = new Money(10.00m, "USD");
        var eur = new Money(10.00m, "EUR");

        // Assert
        usd.ShouldNotBe(eur);
    }
}
```

## Shouldly Assertion Cheat Sheet

Shouldly provides fluent assertions that produce clear failure messages. Here are the most common patterns:

```csharp
// Boolean
result.IsSuccess.ShouldBeTrue();
result.IsFailure.ShouldBeTrue();

// Equality
result.Value.ShouldBe(expectedValue);
result.Value.ShouldNotBe(Guid.Empty);

// Null
result.Value.ShouldNotBeNull();
product.ShouldBeNull();

// Type
domainEvent.ShouldBeOfType<ProductCreatedEvent>();

// Collections
result.Errors.ShouldBeEmpty();
result.Errors.ShouldHaveSingleItem();
result.Errors.ShouldContain(e => e.Code == "Product.NotFound");
product.DomainEvents.Count.ShouldBe(2);

// Exceptions
Should.Throw<DomainException>(() => new Money(-1, "USD"));
await Should.ThrowAsync<InvalidOperationException>(
    () => handler.Handle(command, CancellationToken.None));

// Strings
product.Name.ShouldStartWith("Widget");
product.Sku.ShouldNotBeNullOrWhiteSpace();
```

## Best Practices

- **One assertion concept per test.** Each test should verify one behavior. Multiple `ShouldBe` calls are fine if they assert the same concept (e.g., checking all properties of a DTO).
- **Use the Arrange-Act-Assert pattern.** Structure every test with clear sections separated by comments.
- **Name tests descriptively.** Use `Method_Scenario_ExpectedBehavior` naming: `Handle_DuplicateSku_ReturnsConflictError`.
- **Test failure paths, not just success.** For every handler, test both the happy path and each failure branch.
- **Do not mock what you do not own.** Mock your own interfaces (`IRepository<T>`, `IUnitOfWork`), not third-party types like `DbContext`.
- **Keep tests fast.** Unit tests should have no I/O, no database, no network. If a test needs infrastructure, it belongs in integration tests.

## See Also

- [Architecture Tests](./architecture-tests) -- Enforce structural conventions
- [Integration Testing](./integration-testing) -- Test with real infrastructure
- [Result Pattern](/mediator/result-pattern) -- `Result`, `Result<T>`, and `Error` types
- [Pipeline Behaviors](/mediator/pipeline-behaviors) -- Validation pipeline and FluentValidation
