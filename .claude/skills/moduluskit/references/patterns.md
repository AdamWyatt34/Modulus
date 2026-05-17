# ModulusKit Patterns Reference

## Contents
- Result pattern and implicit conversions
- Error types and HTTP mapping
- Pipeline behaviors
- Integration events and outbox pattern
- Source-generated handler registration
- Roslyn analyzers (MOD001-MOD005)
- WARNING: Anti-patterns

---

## Result Pattern

Every handler returns `Result` or `Result<T>` — never throw exceptions for expected errors.

### Implicit conversions for concise returns

```csharp
public async Task<Result<User>> Handle(GetUserQuery query, CancellationToken ct)
{
    var user = await repo.FindAsync(query.UserId, ct);

    // Implicit: TValue → Result<TValue>
    if (user is not null)
        return user;

    // Implicit: Error → Result<TValue>
    return Error.NotFound("Users.NotFound", $"User {query.UserId} not found.");
}
```

### Checking results

```csharp
var result = await mediator.Send(command, ct);

// Boolean check
if (result.IsSuccess) { /* ... */ }
if (result.IsFailure) { /* ... */ }

// Pattern match (recommended for endpoints)
return result.Match(
    value => Results.Ok(value),
    failure => Results.Problem(failure.Errors.First().Description));

// Access value (throws if failed)
var value = result.Value;

// Access errors
foreach (var error in result.Errors)
    logger.LogWarning("{Code}: {Description}", error.Code, error.Description);
```

### ValidationResult

When `ValidationBehavior` detects validation failures, it returns a `ValidationResult` (subclass of `Result`):

```csharp
var result = await mediator.Send(command, ct);

if (result is ValidationResult validation)
{
    // validation.Errors contains all FluentValidation errors
    // Each error has Type == ErrorType.Validation
}
```

---

## Error Types and HTTP Mapping

```csharp
// Factory methods on Error record struct
Error.Validation(code, description)   // → 400 Bad Request
Error.NotFound(code, description)     // → 404 Not Found
Error.Conflict(code, description)     // → 409 Conflict
Error.Unauthorized(code, description) // → 401 Unauthorized
Error.Forbidden(code, description)    // → 403 Forbidden
Error.Failure(code, description)      // → 500 Internal Server Error
```

Map to HTTP in Minimal API endpoints:

```csharp
app.MapGet("/orders/{id}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Query(new GetOrderQuery(id), ct);
    return result.Match(
        order => Results.Ok(order),
        failure => failure.Errors.First().Type switch
        {
            ErrorType.NotFound => Results.NotFound(),
            ErrorType.Validation => Results.BadRequest(failure.Errors),
            ErrorType.Unauthorized => Results.Unauthorized(),
            ErrorType.Forbidden => Results.Forbid(),
            _ => Results.Problem(failure.Errors.First().Description)
        });
});
```

---

## Pipeline Behaviors

Behaviors wrap every command and query (not streaming queries). They execute in registration order (first = outermost):

```
Request → UnhandledExceptionBehavior → LoggingBehavior → ValidationBehavior → Handler → Result
```

### Built-in behaviors

| Behavior | Package | Purpose |
|----------|---------|---------|
| `UnhandledExceptionBehavior<,>` | `ModulusKit.Mediator` | Catches unhandled exceptions, returns `Error.Failure` |
| `LoggingBehavior<,>` | `ModulusKit.Mediator` | Logs request name, elapsed time, success/failure |
| `ValidationBehavior<,>` | `ModulusKit.Mediator` | Runs FluentValidation validators, short-circuits on failure |
| `MetricsBehavior<,>` | `ModulusKit.Mediator` | Records handler duration via `System.Diagnostics.Metrics` |

### Registration order matters

```csharp
// Outermost → innermost
builder.Services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
// MetricsBehavior is optional
builder.Services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
```

### Custom pipeline behavior

```csharp
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser user)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
            return ResultFactory.CreateFailureResult<TResponse>(
                Error.Unauthorized("Auth.Required", "Authentication required."));

        return await next();
    }
}
```

---

## Integration Events and Outbox Pattern

### Event flow

```
Handler writes to DB + Outbox (same transaction)
    → OutboxProcessor polls for pending messages
    → Publishes to broker (RabbitMQ / Azure Service Bus / InMemory)
    → Consumer receives and checks Inbox for idempotency
    → Handler processes the event
```

### Defining events — always use the record base class

```csharp
// GOOD — inherits EventId, OccurredOn, CorrelationId with defaults
public sealed record OrderShippedEvent(Guid OrderId, string TrackingNumber)
    : IntegrationEvent;

// BAD — implementing IIntegrationEvent directly requires manual property setup
public sealed record OrderShippedEvent(...) : IIntegrationEvent { ... }
```

### Transport configuration

```csharp
builder.Services.AddModulusMessaging(options =>
{
    // Development
    options.Transport = Transport.InMemory;

    // Production — RabbitMQ
    options.Transport = Transport.RabbitMq;
    options.ConnectionString = "amqp://guest:guest@localhost:5672";

    // Production — Azure Service Bus
    options.Transport = Transport.AzureServiceBus;
    options.ConnectionString = "Endpoint=sb://...";

    // Assembly scanning — add all assemblies containing events and handlers
    options.Assemblies.Add(typeof(OrderPlacedEvent).Assembly);

    // Tuning (optional)
    options.OutboxBatchSize = 100;         // 1-1000, default 100
    options.OutboxPollInterval = TimeSpan.FromSeconds(5);  // min 1s, default 5s
});
```

### Outbox — atomic with business data

```csharp
public sealed class PlaceOrderHandler(AppDbContext db, IOutboxStore outbox)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        db.Orders.Add(order);

        // Save event to outbox within the SAME DbContext transaction
        await outbox.Save(new OrderPlacedEvent(order.Id, cmd.CustomerId, order.Total), ct);

        await db.SaveChangesAsync(ct);  // both order + outbox message committed atomically
        return order.Id;
    }
}
```

### Domain events vs integration events

| | Domain Events | Integration Events |
|---|---|---|
| Scope | In-process, within a module | Cross-module, cross-process |
| Interface | `IDomainEvent` | `IIntegrationEvent` |
| Dispatch | `IMediator.Publish()` (synchronous) | `IOutboxStore.Save()` → broker |
| Delivery | Immediate, same transaction | Eventually consistent, guaranteed |
| Use for | Side effects within the same module | Notifying other modules |

---

## Source-Generated Handler Registration

The `ModulusKit.Generators` package scans your assembly at compile time and generates `AddModulusHandlers()`:

```csharp
// Auto-generated — never write this manually
public static IServiceCollection AddModulusHandlers(this IServiceCollection services)
{
    // Commands
    services.AddScoped<ICommandHandler<PlaceOrderCommand>, PlaceOrderHandler>();
    services.AddScoped<ICommandHandler<CreateProductCommand, Guid>, CreateProductHandler>();

    // Queries
    services.AddScoped<IQueryHandler<GetOrderQuery, OrderDto>, GetOrderHandler>();

    // Validators
    services.AddScoped<IValidator<PlaceOrderCommand>, PlaceOrderValidator>();

    // Domain Events
    services.AddScoped<IDomainEventHandler<OrderPlacedDomainEvent>, AuditLogHandler>();

    return services;
}
```

It discovers:
- `ICommandHandler<T>` and `ICommandHandler<T, TResult>`
- `IQueryHandler<T, TResult>`
- `IStreamQueryHandler<T, TResult>`
- `IDomainEventHandler<T>`
- `IIntegrationEventHandler<T>`
- `AbstractValidator<T>` (FluentValidation)

---

## Roslyn Analyzers (MOD001-MOD005)

Install `ModulusKit.Analyzers` for compile-time enforcement:

| Rule | Severity | What It Catches |
|------|----------|-----------------|
| MOD001 | Error | Cross-module reference to non-Integration project |
| MOD002 | Warning | Handler not returning `Result` or `Result<T>` |
| MOD003 | Warning | Throwing exceptions for expected errors (has code fix) |
| MOD004 | Warning | Infrastructure attributes (`[Column]`, `[JsonProperty]`) in Domain layer |
| MOD005 | Info | Public setter on entity property (has code fix) |

Suppress with `#pragma warning disable MOD001` or `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.MOD001.severity = none
```

---

## WARNING: Anti-Patterns

### WARNING: Throwing exceptions instead of returning errors

```csharp
// BAD — pipeline cannot handle this; bypasses UnhandledExceptionBehavior intent
public async Task<Result<Order>> Handle(GetOrderQuery query, CancellationToken ct)
{
    var order = await repo.FindAsync(query.Id, ct);
    if (order is null)
        throw new NotFoundException("Order not found");  // ❌
    return order;
}

// GOOD — use implicit conversion
public async Task<Result<Order>> Handle(GetOrderQuery query, CancellationToken ct)
{
    var order = await repo.FindAsync(query.Id, ct);
    if (order is null)
        return Error.NotFound("Orders.NotFound", "Order not found");  // ✅
    return order;
}
```

### WARNING: Publishing integration events via IMessageBus directly

```csharp
// BAD — not transactional; if the DB commit fails, the event is already published
await messageBus.Publish(new OrderPlacedEvent(...), ct);

// GOOD — stored in outbox, published after transaction commits
await outboxStore.Save(new OrderPlacedEvent(...), ct);
await dbContext.SaveChangesAsync(ct);
```

### WARNING: Using domain events for cross-module communication

```csharp
// BAD — domain events are in-process only; the other module won't receive this
await mediator.Publish(new OrderPlacedDomainEvent(order.Id), ct);

// GOOD — use integration events for cross-module communication
await outboxStore.Save(new OrderPlacedEvent(order.Id, ...), ct);
```

### WARNING: Manually registering handlers

```csharp
// BAD — the source generator already does this
services.AddScoped<ICommandHandler<PlaceOrderCommand>, PlaceOrderHandler>();

// GOOD — one call discovers everything
services.AddModulusHandlers();
```

### WARNING: Returning raw values without Result wrapper

```csharp
// BAD — handler interface requires Result<T>, not T
public async Task<Order> Handle(GetOrderQuery query, CancellationToken ct)  // ❌ wrong return type

// GOOD — implicit conversion wraps automatically
public async Task<Result<Order>> Handle(GetOrderQuery query, CancellationToken ct)
{
    return await repo.FindAsync(query.Id, ct);  // ✅ implicit TValue → Result<TValue>
}
```
