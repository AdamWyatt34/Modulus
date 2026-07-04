# ModulusKit Workflows Reference

## Contents
- CLI command reference
- Adding a command end-to-end
- Adding a query end-to-end
- Adding an integration event end-to-end
- DI registration checklist
- Testing with ModulusKit
- Troubleshooting

---

## CLI Command Reference

```powershell
# Install the CLI tool
dotnet tool install -g ModulusKit.Cli
```

### Scaffolding

```powershell
modulus init MyApp                                   # new solution
modulus init MyApp --aspire                          # with Aspire orchestration
modulus init MyApp --transport rabbitmq              # inmemory | rabbitmq | azureservicebus
modulus init MyApp --output ./src --no-git

modulus add-module Orders                            # full module (Domain/Application/Infrastructure/Integration/Api)
modulus add-module SharedKernel --no-endpoints

modulus add-entity Order --module Orders --properties "CustomerId:Guid,Status:string,Total:decimal"
modulus add-entity Order --module Orders --aggregate --id-type guid

modulus add-command CreateOrder --module Orders --result-type Guid
modulus add-query GetOrders --module Orders --result-type "List<OrderDto>"
modulus add-endpoint CreateOrder --module Orders --method POST --route "/orders" --command CreateOrderCommand

modulus add-event OrderPlaced --module Orders --properties "OrderId:Guid,Total:decimal"
modulus add-consumer OrderPlaced --module Loyalty --event-module Orders   # handler in Loyalty for Orders' event

modulus remove-module Orders                         # dry-run by default; --confirm to apply, --force to override blocks
```

### Inspection and operations

```powershell
modulus list-modules --json                          # all list commands support --json
modulus list-events
modulus list-consumers
modulus list-entities

modulus doctor                                       # six solution-health checks; --json, --strict (warnings fail)

modulus outbox list-failed                           # dead-lettered outbox rows
modulus outbox retry --message-id <guid>             # options: --connection-string, --config, --provider, --max-attempts

modulus dlq list --transport rabbitmq                # peek broker dead-letter queue
modulus dlq replay --transport rabbitmq --message-id <guid>   # or --all; ASB also needs --event
                                                     # options: --connection-string, --config, --endpoint, --max

modulus upgrade                                      # bump all ModulusKit.* pins to the CLI's version
modulus upgrade --version 2.1.0 --dry-run            # preview a specific version
```

---

## Adding a Command End-to-End

Copy this checklist:

- [ ] Step 1: Define the command record
- [ ] Step 2: Implement the handler
- [ ] Step 3: Add a FluentValidation validator (optional)
- [ ] Step 4: Wire the endpoint
- [ ] Step 5: Verify `AddModulusHandlers()` discovers it (rebuild)

Or scaffold steps 1–2 with `modulus add-command CreateOrder --module Orders --result-type Guid`.

### Step 1 — Command record

```csharp
// Application/Commands/CreateOrderCommand.cs
namespace MyApp.Orders.Application.Commands;

public record CreateOrderCommand(Guid CustomerId, List<OrderItemDto> Items) : ICommand<Guid>;
```

Choose `ICommand` (no return) or `ICommand<TResult>` (returns a value).

### Step 2 — Handler

```csharp
// Application/Commands/CreateOrderCommandHandler.cs
namespace MyApp.Orders.Application.Commands;

public sealed class CreateOrderCommandHandler(
    IOrderRepository repo,
    IUnitOfWork uow,
    IOutboxStore outbox) : ICommandHandler<CreateOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        // Validate business rules
        if (command.Items.Count == 0)
            return Error.Validation("Orders.NoItems", "At least one item required.");

        // Create entity
        var order = Order.Create(command.CustomerId, command.Items);
        await repo.AddAsync(order, ct);

        // Publish integration event via outbox (same transaction)
        await outbox.Save(new OrderCreatedEvent(order.Id, command.CustomerId), ct);

        await uow.SaveChangesAsync(ct);
        return order.Id;  // implicit conversion to Result<Guid>
    }
}
```

With `UnitOfWorkBehavior<,>` registered, the explicit `uow.SaveChangesAsync(ct)` call can be omitted — the behavior commits after the handler returns success.

### Step 3 — Validator (optional)

```csharp
// Application/Commands/CreateOrderCommandValidator.cs
namespace MyApp.Orders.Application.Commands;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one item.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}
```

The source generator discovers `AbstractValidator<T>` automatically — no manual registration.

### Step 4 — Endpoint

Scaffolded solutions use the `IEndpoint` pattern — implementations are assembly-scanned and mapped by the generated `Map{Module}Endpoints()`:

```csharp
// Api/Endpoints/CreateOrderEndpoint.cs
namespace MyApp.Orders.Api.Endpoints;

public sealed class CreateOrderEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/orders", async (
            CreateOrderCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.Match(
                id => Results.Created($"/api/orders/{id}", id),
                failure => failure.Errors.First().Type switch
                {
                    ErrorType.Validation => Results.BadRequest(failure.Errors),
                    _ => Results.Problem(failure.Errors.First().Description)
                });
        });
    }
}
```

(Or scaffold it: `modulus add-endpoint CreateOrder --module Orders --method POST --route "/orders" --command CreateOrderCommand`.)

### Step 5 — Verify discovery

Rebuild the project. The source generator emits `AddModulusHandlers()` containing:
```csharp
services.AddScoped<ICommandHandler<CreateOrderCommand, Guid>, CreateOrderCommandHandler>();
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

If the handler doesn't appear, check:
- Does it implement `ICommandHandler<T>` or `ICommandHandler<T, TResult>`?
- Is the class `public` (not `internal` in a different assembly)?
- Did you rebuild after adding the handler?

---

## Adding a Query End-to-End

```csharp
// 1. Define query
public record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

// 2. Implement handler
public sealed class GetOrderQueryHandler(IQueryDb db)
    : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await db.Set<Order>()
            .Where(o => o.Id == query.OrderId)
            .Select(o => new OrderDto(o.Id, o.CustomerId, o.Status, o.Total))
            .FirstOrDefaultAsync(ct);

        if (order is null)
            return Error.NotFound("Orders.NotFound", $"Order {query.OrderId} not found.");

        return order;
    }
}

// 3. Wire endpoint
app.MapGet("/api/orders/{id}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Query(new GetOrderQuery(id), ct);
    return result.Match(Results.Ok, failure => Results.NotFound());
});
```

### Streaming queries

```csharp
// Bypasses the pipeline — no validation/logging behaviors
public record StreamOrdersQuery(Guid CustomerId) : IStreamQuery<OrderDto>;

public sealed class StreamOrdersHandler(IQueryDb db)
    : IStreamQueryHandler<StreamOrdersQuery, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> Handle(
        StreamOrdersQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var order in db.Set<Order>()
            .Where(o => o.CustomerId == query.CustomerId)
            .Select(o => new OrderDto(o.Id, o.CustomerId, o.Status, o.Total))
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            yield return order;
        }
    }
}

// Dispatch
await foreach (var order in mediator.Stream(new StreamOrdersQuery(customerId), ct))
{
    // process each order
}
```

---

## Adding an Integration Event End-to-End

- [ ] Step 1: Define the event record in the module's Integration project
- [ ] Step 2: Publish via `IOutboxStore` in the source module
- [ ] Step 3: Handle in the consuming module
- [ ] Step 4: Add consuming module's assembly to `MessagingOptions.Assemblies`
- [ ] Step 5: Verify consumer is discovered

Or scaffold steps 1 and 3: `modulus add-event OrderCreated --module Orders --properties "OrderId:Guid,CustomerId:Guid"` then `modulus add-consumer OrderCreated --module Notifications --event-module Orders` (cross-module Integration-only references are wired automatically).

### Step 1 — Event

```csharp
// MyApp.Orders.Integration/IntegrationEvents/OrderCreatedEvent.cs
namespace MyApp.Orders.IntegrationEvents;

public sealed record OrderCreatedEvent(Guid OrderId, Guid CustomerId)
    : IntegrationEvent;
```

### Step 2 — Publish

```csharp
// Inside a command handler in the Orders module
await outbox.Save(new OrderCreatedEvent(order.Id, command.CustomerId), ct);
await dbContext.SaveChangesAsync(ct);  // atomic commit; wakes the outbox processor immediately
```

### Step 3 — Handle

```csharp
// MyApp.Notifications/Handlers/OrderCreatedEventHandler.cs
namespace MyApp.Notifications.Handlers;

public sealed class OrderCreatedEventHandler(IEmailService email)
    : IIntegrationEventHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent @event, CancellationToken ct)
    {
        await email.SendOrderConfirmationAsync(@event.CustomerId, @event.OrderId, ct);
    }
}
```

### Step 4 — Register assemblies

```csharp
builder.Services.AddModulusRabbitMqTransport();
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Transport = Transport.RabbitMq;
    options.ConnectionString ??= builder.Configuration.GetConnectionString("RabbitMq");
    options.Assemblies.Add(typeof(OrderCreatedEvent).Assembly);
    options.Assemblies.Add(typeof(OrderCreatedEventHandler).Assembly);
});
```

---

## DI Registration Checklist

```csharp
// Program.cs — complete registration
var builder = WebApplication.CreateBuilder(args);

// 1. Mediator core (registers IMediator only — behaviors are all opt-in)
builder.Services.AddModulusMediator();

// 2. Source-generated handler discovery
builder.Services.AddModulusHandlers();

// 3. Pipeline behaviors (order = outermost → innermost)
builder.Services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(UnitOfWorkBehavior<,>));

// 4. Messaging (optional — only if using integration events)
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    options.Transport = Transport.InMemory;   // broker transports need their package's Add*Transport() call
    options.Assemblies.Add(typeof(Program).Assembly);
});
builder.Services.AddModulusOutbox(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddModulusInbox(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// 5. Messaging health checks (optional; tagged "ready" for /readyz gating)
builder.Services.AddHealthChecks().AddModulusMessaging();

// 6. Your DbContext (ModulusKit does NOT register this — you do). Attach the outbox
//    interceptor if this context maps the outbox table, so committed rows dispatch immediately.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<OutboxNotifyingInterceptor>());
});

var app = builder.Build();
```

In a scaffolded solution, module registration is source-generated instead: `builder.Services.AddAllModules(builder.Configuration)` + `app.MapAllModuleEndpoints()`.

---

## Testing with ModulusKit

### Unit testing a command handler

```csharp
public sealed class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithId()
    {
        // Arrange
        var repo = new FakeOrderRepository();
        var uow = new FakeUnitOfWork();
        var outbox = new FakeOutboxStore();
        var handler = new CreateOrderCommandHandler(repo, uow, outbox);

        var command = new CreateOrderCommand(Guid.NewGuid(), [new OrderItemDto(Guid.NewGuid(), 2)]);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
        repo.Added.Count.ShouldBe(1);
        outbox.SavedEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_EmptyItems_ReturnsValidationError()
    {
        var handler = new CreateOrderCommandHandler(
            new FakeOrderRepository(), new FakeUnitOfWork(), new FakeOutboxStore());

        var result = await handler.Handle(
            new CreateOrderCommand(Guid.NewGuid(), []),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Errors.First().Code.ShouldBe("Orders.NoItems");
    }
}
```

### Testing through the full mediator pipeline

```csharp
public sealed class MediatorIntegrationTests
{
    [Fact]
    public async Task Send_WithValidation_ReturnsValidationResult()
    {
        // Arrange — full pipeline with validation behavior
        var services = new ServiceCollection();
        services.AddModulusMediator();
        services.AddModulusHandlers();
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        // Register your DbContext, repositories, etc.

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act — send invalid command (triggers FluentValidation)
        var result = await mediator.Send(new CreateOrderCommand(Guid.Empty, []));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.ShouldBeOfType<ValidationResult>();
        result.Errors.ShouldContain(e => e.Code == "CustomerId");
    }
}
```

### Testing integration event handlers

```csharp
public sealed class OrderCreatedEventHandlerTests
{
    [Fact]
    public async Task Handle_SendsConfirmationEmail()
    {
        var emailService = new FakeEmailService();
        var handler = new OrderCreatedEventHandler(emailService);

        await handler.Handle(
            new OrderCreatedEvent(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        emailService.SentEmails.Count.ShouldBe(1);
    }
}
```

### Test conventions

- Use **Shouldly** for assertions, not raw `Assert`
- Use **hand-written fakes** (e.g., `FakeOrderRepository`), not Moq/NSubstitute
- Name tests `Method_Scenario_Expected`
- Make test classes `sealed`
- Use `Arrange / Act / Assert` comment sections

---

## Troubleshooting

### "Handler not discovered by source generator"
- Ensure the class implements the correct interface (`ICommandHandler<T>`, etc.)
- Ensure the class is `public`, not `internal`
- Rebuild the solution — source generators run at compile time
- Check the generated file in `obj/Debug/net10.0/generated/`

### "Validator not running"
- Ensure `ValidationBehavior<,>` is registered via `AddPipelineBehavior`
- Ensure the validator class inherits `AbstractValidator<T>` where `T` matches the command/query type
- Ensure `AddModulusHandlers()` is called — it registers validators too

### "Integration event handler never called"
- Ensure the handler's assembly is in `MessagingOptions.Assemblies`
- Ensure you called `await outbox.Save(...)` + `await dbContext.SaveChangesAsync()`
- Ensure `OutboxProcessor` is running (registered via `AddModulusMessaging`) and `AddModulusOutbox` is registered
- For broker transports, ensure the transport package's `Add*Transport()` call is present
- Check logs for `OutboxProcessor` warnings about unresolvable types
- Run `modulus outbox list-failed` to see dead-lettered outbox rows, and `modulus dlq list` for broker dead-letters

### "Events sit in the outbox for seconds before publishing"
- Committed rows should dispatch immediately via the wake signal; if latency equals `OutboxPollInterval`, the writing DbContext is missing `OutboxNotifyingInterceptor` (or the writer is a different process — expected fallback)
- Check the `modulus.messaging.outbox.wakeups` metric: only `poll` reasons means no signals arrive

### "MOD001 false positive on shared types"
- MOD001 allows references to `*.Integration` projects
- Put shared DTOs and event contracts in the module's `Integration` project
- Suppress with `#pragma warning disable MOD001` if truly intentional

### "Package version skew after upgrading"
- All nine ModulusKit.* packages ship at one aligned version
- Run `modulus upgrade` to bump every pin in `Directory.Packages.props`, then `dotnet restore`
- Run `modulus doctor` — it warns when pins don't match the CLI version
