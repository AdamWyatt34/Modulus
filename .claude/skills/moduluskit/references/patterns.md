# ModulusKit Patterns Reference

## Contents
- Result pattern and implicit conversions
- Error types and HTTP mapping
- Pipeline behaviors
- Integration events and outbox pattern
- Immediate outbox dispatch (change notification)
- Inbox pattern (consumer idempotency)
- Health checks and metrics
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
// Result:     Match<TOut>(Func<TOut> onSuccess, Func<Result, TOut> onFailure)
// Result<T>:  Match<TOut>(Func<T, TOut> onSuccess, Func<Result<T>, TOut> onFailure)
return result.Match(
    value => Results.Ok(value),
    failure => Results.Problem(failure.Errors.First().Description));

// Access value (throws if failed)
var value = result.Value;

// Access errors (IReadOnlyList<Error>)
foreach (var error in result.Errors)
    logger.LogWarning("{Code}: {Description}", error.Code, error.Description);
```

### ValidationResult

When `ValidationBehavior` detects validation failures, it returns a `ValidationResult` (subclass of `Result`; `ValidationResult<T>` for valued requests):

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

`Error` is a `readonly record struct` with `Code`, `Description`, and `Type`:

```csharp
// Factory methods — all (string code, string description)
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

Behaviors wrap every command and query (not streaming queries — those bypass the pipeline). `AddModulusMediator()` registers **no behaviors**; each is opt-in via `AddPipelineBehavior`, and registration order = execution order (first = outermost). Canonical order:

```
Request → UnhandledExceptionBehavior → LoggingBehavior → MetricsBehavior → ValidationBehavior → UnitOfWorkBehavior → Handler → Result
```

### Built-in behaviors (all in `ModulusKit.Mediator`, all opt-in)

| Behavior | Purpose |
|----------|---------|
| `UnhandledExceptionBehavior<,>` | Catches unhandled exceptions, returns generic `Error.Failure` (details logged, never exposed to callers) |
| `LoggingBehavior<,>` | Logs request name, elapsed time, success/failure |
| `TracingBehavior<,>` | `ActivitySource` "Modulus.Mediator" spans with request/outcome/error tags |
| `MetricsBehavior<,>` | Records handler duration via `System.Diagnostics.Metrics` |
| `ValidationBehavior<,>` | Runs FluentValidation validators, short-circuits with `ValidationResult` on failure |
| `UnitOfWorkBehavior<,>` | Calls `IUnitOfWork.SaveChangesAsync` after a **successful command** (no-op for queries or when `IUnitOfWork` is unregistered) |

### Registration order matters

```csharp
// Outermost → innermost
builder.Services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(MetricsBehavior<,>));      // optional
builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(UnitOfWorkBehavior<,>));   // innermost — commits closest to the handler
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
    → commit wakes the OutboxProcessor immediately (poll interval = fallback sweep)
    → publishes to broker (RabbitMQ / Azure Service Bus / InMemory)
    → consumer receives; inbox reserves per (event, handler) pair for idempotency
    → handler processes the event exactly once
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

Broker transports live in separate packages and need one registration call each; the in-memory transport is built into `ModulusKit.Messaging`.

```csharp
// RabbitMQ (package: ModulusKit.Messaging.RabbitMq)
builder.Services.AddModulusRabbitMqTransport();

// Azure Service Bus (package: ModulusKit.Messaging.AzureServiceBus)
builder.Services.AddModulusAzureServiceBusTransport();

builder.Services.AddModulusMessaging(builder.Configuration, options =>  // binds "Messaging" config section, then callback
{
    options.Transport = Transport.RabbitMq;   // InMemory | RabbitMq | AzureServiceBus
    options.ConnectionString ??= builder.Configuration.GetConnectionString("RabbitMq");
    // Azure managed identity alternative: options.FullyQualifiedNamespace + options.Credential

    // Assembly scanning — add all assemblies containing events and handlers
    options.Assemblies.Add(typeof(OrderPlacedEvent).Assembly);

    // Endpoint identity + broker tuning (optional)
    options.EndpointName = "orders-service";  // queue/subscription name; replicas sharing it compete
    options.PrefetchCount = 10;               // 1-1000
    options.AutoProvision = true;             // false for least-privilege, pre-declared topology

    // Outbox tuning (optional)
    options.OutboxBatchSize = 100;                          // 1-1000, default 100
    options.OutboxPollInterval = TimeSpan.FromSeconds(30);  // FALLBACK sweep — signaled rows dispatch immediately

    // Retries: RetryPolicy (outbox dispatch) and ConsumerRetry (handler execution), both default 5 attempts
});

// Persistence — required for outbox publishing and inbox idempotency respectively
builder.Services.AddModulusOutbox(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddModulusInbox(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
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
| Interface | `IDomainEvent` | `IntegrationEvent` base record (`IIntegrationEvent`) |
| Dispatch | `IMediator.Publish()` (synchronous) | `IOutboxStore.Save()` → broker |
| Delivery | Immediate, same transaction | Eventually consistent, guaranteed |
| Use for | Side effects within the same module | Notifying other modules |

---

## Immediate Outbox Dispatch (Change Notification)

New outbox rows wake the `OutboxProcessor` the moment they commit — dispatch latency is milliseconds, not the poll interval. Polling survives only as the fallback sweep (crash recovery, other replicas, external writers).

- `AddModulusOutbox` auto-attaches the notifying interceptor to the library's `OutboxDbContext` — nothing to do.
- CLI-scaffolded module DbContexts come pre-wired.
- For your own DbContext that maps the outbox table:

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<OutboxNotifyingInterceptor>());
});
```

- Inside EF-managed transactions the signal fires at **commit time** (rollback never signals). Ambient `TransactionScope` and externally-owned transactions fall back to the poll sweep.
- `IOutboxNotifier` (singleton; `Notify()` / `WaitAsync`) is the extension point for external CDC listeners — e.g. a PostgreSQL `LISTEN/NOTIFY` hosted service calls `Notify()`.
- Because signaled rows dispatch immediately, treat `OutboxPollInterval` as a fallback knob — raising it to ~30s cuts idle DB load without adding latency.
- The `modulus.messaging.outbox.wakeups` counter (tag `reason`: `signal`/`poll`/`backlog`) shows whether signals actually arrive in a deployment.

---

## Inbox Pattern (Consumer Idempotency)

Register with `AddModulusInbox(...)`. Consumption is **reservation-based**: each (event, handler) pair is atomically reserved before execution and marked processed after, so concurrent duplicate deliveries execute each handler exactly once. A crashed consumer's reservation goes stale after `MessagingOptions.ConsumerReservationTimeout` (default 5 minutes) and is taken over on redelivery — at-least-once delivery is preserved, and `modulus dlq replay` re-runs only handlers that never succeeded. Without `AddModulusInbox`, handlers run with no deduplication. There is no inbox polling loop — dedup happens inline at transport-delivery time.

---

## Health Checks and Metrics

```csharp
// Broker connectivity + outbox backlog depth, tagged ["ready", "messaging"]
builder.Services.AddHealthChecks().AddModulusMessaging();

// Gate readiness on them
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

Checks: `modulus_messaging_transport` (via optional `ITransportHealthProbe`) and `modulus_messaging_outbox` (Degraded/Unhealthy thresholds configurable via `ModulusMessagingHealthCheckOptions`).

Metrics: subscribe with `AddMeter("Modulus.Messaging")` — outbox dispatch outcomes, outbox wakeups, consumer handler duration, inbox dedup, retries, dead-letters. The mediator side has `MetricsBehavior` + `TracingBehavior` (ActivitySource `Modulus.Mediator`).

---

## Source-Generated Handler Registration

The `ModulusKit.Generators` package scans your assembly at compile time and generates `AddModulusHandlers()`:

```csharp
// Auto-generated — never write this manually
public static IServiceCollection AddModulusHandlers(this IServiceCollection services)
{
    services.AddScoped<ICommandHandler<PlaceOrderCommand>, PlaceOrderHandler>();
    services.AddScoped<IQueryHandler<GetOrderQuery, OrderDto>, GetOrderHandler>();
    services.AddScoped<IValidator<PlaceOrderCommand>, PlaceOrderValidator>();
    // ... every handler and validator discovered
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

It also generates module discovery: `AddAllModules(IServiceCollection, IConfiguration)` and `MapAllModuleEndpoints(WebApplication)` from `IModuleRegistration` implementations; control initialization order with `[ModuleOrder(int)]`.

---

## Roslyn Analyzers (MOD001-MOD005)

Install `ModulusKit.Analyzers` for compile-time enforcement:

| Rule | Severity | What It Catches | Code Fix |
|------|----------|-----------------|----------|
| MOD001 | Error | Cross-module reference to non-Integration project | No |
| MOD002 | Warning | Handler not returning `Result` or `Result<T>` | No |
| MOD003 | Warning | Throwing exceptions for expected errors | Yes (`throw` → `return Error`) |
| MOD004 | Warning | Infrastructure attributes (`[Column]`, `[JsonProperty]`) in Domain layer | Yes |
| MOD005 | Info | Public setter on entity property | Yes (adds `private`) |

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

### WARNING: Registering a broker transport without its package call

```csharp
// BAD — Transport.RabbitMq with no transport registration throws at resolution time
builder.Services.AddModulusMessaging(o => o.Transport = Transport.RabbitMq);

// GOOD — one registration per broker package
builder.Services.AddModulusRabbitMqTransport();
builder.Services.AddModulusMessaging(o => { o.Transport = Transport.RabbitMq; /* ... */ });
```
