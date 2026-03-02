# Result Pattern

Every command and query in Modulus returns a `Result` or `Result<T>` instead of throwing exceptions for expected failures. This enables railway-oriented programming where success and failure flow through the same return type, making error handling explicit and composable.

## Result

The `Result` class represents the outcome of an operation that does not return a value:

```csharp
public class Result
{
    // Properties
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public IReadOnlyList<Error> Errors { get; }

    // Factory methods
    public static Result Success();
    public static Result Failure(params Error[] errors);

    // Pattern matching
    public T Match<T>(Func<T> onSuccess, Func<IReadOnlyList<Error>, T> onFailure);
}
```

### Creating Results

```csharp
// Success
return Result.Success();

// Failure with a single error
return Result.Failure(Error.NotFound("Order.NotFound", "Order was not found."));

// Failure with multiple errors
return Result.Failure(
    Error.Validation("Order.InvalidTotal", "Total must be greater than zero."),
    Error.Validation("Order.MissingCustomer", "Customer ID is required."));
```

### Inspecting Results

```csharp
var result = await mediator.Send(command, ct);

if (result.IsSuccess)
{
    // handle success
}

if (result.IsFailure)
{
    foreach (var error in result.Errors)
    {
        logger.LogWarning("Error {Code}: {Description}", error.Code, error.Description);
    }
}
```

## Result\<TValue\>

`Result<TValue>` extends `Result` and adds a typed `Value` property for operations that return data:

```csharp
public class Result<TValue> : Result
{
    // The value (only valid when IsSuccess is true)
    public TValue Value { get; }

    // Factory methods
    public static Result<TValue> Success(TValue value);
    public new static Result<TValue> Failure(params Error[] errors);

    // Pattern matching
    public T Match<T>(Func<TValue, T> onSuccess, Func<IReadOnlyList<Error>, T> onFailure);
}
```

### Creating Typed Results

```csharp
// Success with a value
return Result<Guid>.Success(product.Id);

// Failure
return Result<Guid>.Failure(Error.Conflict("Product.Duplicate", "Product already exists."));
```

## Error

`Error` is a readonly record struct that carries structured error information:

```csharp
public readonly record struct Error(string Code, string Description, ErrorType Type)
{
    // Sentinel value representing "no error"
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    // Factory methods
    public static Error Failure(string code, string description);
    public static Error Validation(string code, string description);
    public static Error NotFound(string code, string description);
    public static Error Conflict(string code, string description);
    public static Error Unauthorized(string code, string description);
    public static Error Forbidden(string code, string description);
}
```

### Error Factory Methods

Each factory method creates an error with the appropriate `ErrorType`:

```csharp
// General failure (server error, unexpected condition)
Error.Failure("Payment.GatewayError", "Payment gateway returned an unexpected response.");

// Validation error (invalid input)
Error.Validation("Order.InvalidQuantity", "Quantity must be at least 1.");

// Not found (resource does not exist)
Error.NotFound("Product.NotFound", $"Product with ID {id} was not found.");

// Conflict (duplicate, concurrent modification)
Error.Conflict("Product.DuplicateSku", $"A product with SKU '{sku}' already exists.");

// Unauthorized (not authenticated)
Error.Unauthorized("Auth.TokenExpired", "The authentication token has expired.");

// Forbidden (authenticated but not authorized)
Error.Forbidden("Auth.InsufficientRole", "You do not have permission to perform this action.");
```

## ErrorType Enum

The `ErrorType` enum classifies errors into categories that map cleanly to HTTP status codes:

```csharp
public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden
}
```

### Error-to-HTTP Status Code Mapping

| ErrorType | HTTP Status | Typical Use |
|---|---|---|
| `Validation` | 400 Bad Request | Invalid input, failed business rules |
| `Unauthorized` | 401 Unauthorized | Missing or expired authentication |
| `Forbidden` | 403 Forbidden | Authenticated but insufficient permissions |
| `NotFound` | 404 Not Found | Resource does not exist |
| `Conflict` | 409 Conflict | Duplicate resource, concurrent modification |
| `Failure` | 500 Internal Server Error | Unexpected failures, infrastructure errors |

::: tip Mapping errors to HTTP responses
Use this table to translate `ErrorType` values into the correct HTTP status code in your API endpoints. The `Match` method makes this straightforward -- see the [Match Pattern](#match-pattern) section below.
:::

## Implicit Conversions

The Result types define implicit conversion operators that reduce boilerplate. Instead of explicitly calling factory methods, you can return values and errors directly.

### Error to Result

```csharp
// Instead of this:
return Result.Failure(Error.NotFound("Order.NotFound", "Order not found."));

// You can write:
return Error.NotFound("Order.NotFound", "Order not found.");
```

### TValue to Result\<TValue\>

```csharp
// Instead of this:
return Result<Guid>.Success(product.Id);

// You can write:
return product.Id;
```

### Error to Result\<TValue\>

```csharp
// Instead of this:
return Result<ProductDto>.Failure(Error.NotFound("Product.NotFound", "Not found."));

// You can write:
return Error.NotFound("Product.NotFound", "Not found.");
```

### Practical Example

These implicit conversions make handlers clean and concise:

```csharp
public sealed class CreateOrderCommandHandler
    : ICommandHandler<CreateOrderCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;

    public CreateOrderCommandHandler(
        IOrderRepository orderRepository,
        ICustomerRepository customerRepository)
    {
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
    }

    public async Task<Result<Guid>> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(
            command.CustomerId, cancellationToken);

        if (customer is null)
        {
            // Implicit Error -> Result<Guid>
            return Error.NotFound(
                "Customer.NotFound",
                $"Customer with ID {command.CustomerId} was not found.");
        }

        var order = new Order(customer.Id, command.Items);

        await _orderRepository.AddAsync(order, cancellationToken);

        // Implicit Guid -> Result<Guid>
        return order.Id;
    }
}
```

## Match Pattern

The `Match` method enables railway-oriented programming by forcing you to handle both the success and failure paths:

### Result.Match

```csharp
var result = await mediator.Send(new DeleteProductCommand(id), ct);

// Match with two branches
var response = result.Match(
    onSuccess: () => Results.NoContent(),
    onFailure: errors => Results.BadRequest(errors));
```

### Result\<T\>.Match

```csharp
var result = await mediator.Query(new GetProductByIdQuery(id), ct);

// Match with typed value
var response = result.Match(
    onSuccess: product => Results.Ok(product),
    onFailure: errors => Results.NotFound(errors));
```

### Full Endpoint Example with Error-to-HTTP Mapping

```csharp
public static async Task<IResult> HandleCreateProduct(
    CreateProductCommand command,
    IMediator mediator,
    CancellationToken ct)
{
    var result = await mediator.Send(command, ct);

    return result.Match(
        onSuccess: id => Results.Created($"/products/{id}", new { id }),
        onFailure: errors =>
        {
            var firstError = errors[0];

            return firstError.Type switch
            {
                ErrorType.Validation => Results.BadRequest(errors),
                ErrorType.Unauthorized => Results.Unauthorized(),
                ErrorType.Forbidden => Results.Forbid(),
                ErrorType.NotFound => Results.NotFound(errors),
                ErrorType.Conflict => Results.Conflict(errors),
                _ => Results.StatusCode(500)
            };
        });
}
```

## ValidationResult

`ValidationResult` and `ValidationResult<TValue>` are specialized result types designed for the validation pipeline behavior. They carry multiple validation errors and are created with the `WithErrors` factory method:

```csharp
public class ValidationResult : Result
{
    public static ValidationResult WithErrors(Error[] errors);
}

public class ValidationResult<TValue> : Result<TValue>
{
    public static ValidationResult<TValue> WithErrors(Error[] errors);
}
```

### Usage in the Validation Pipeline

The built-in `ValidationBehavior` uses these types when FluentValidation validators report errors:

```csharp
// Inside ValidationBehavior (for reference -- you don't write this yourself)
var validationErrors = failures
    .Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))
    .ToArray();

return ValidationResult<TResponse>.WithErrors(validationErrors);
```

::: info You rarely create ValidationResult directly
`ValidationResult` is primarily used internally by the `ValidationBehavior` pipeline behavior. Your handlers return `Result` or `Result<T>` as usual. The validation pipeline short-circuits with a `ValidationResult` before your handler is ever called if validation fails.
:::

## Best Practices

- **Prefer implicit conversions.** Return errors and values directly instead of calling factory methods. It produces cleaner handler code.
- **Use specific error types.** Choose the most specific `ErrorType` for each failure. `Error.NotFound()` is more useful than a generic `Error.Failure()` because it maps directly to HTTP 404.
- **Use descriptive error codes.** Follow the `Entity.ErrorKind` convention (e.g., `Product.NotFound`, `Order.InvalidTotal`). These codes are useful for client-side error handling and localization.
- **Handle all branches with Match.** The `Match` method forces you to deal with both success and failure, preventing silent error swallowing.
- **Use ValidationResult for aggregate validation.** When you need to collect and return multiple validation errors at once, the `ValidationBehavior` and `ValidationResult` handle this automatically via FluentValidation.

## See Also

- [Commands & Queries](./commands-queries) -- Define commands and queries that return Results
- [Pipeline Behaviors](./pipeline-behaviors) -- ValidationBehavior and other pipeline stages
