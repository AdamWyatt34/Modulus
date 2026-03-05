# Handler Registration

The Handler Registration generator discovers all command handlers, query handlers, domain event handlers, integration event handlers, and FluentValidation validators in your project at compile time. It produces an `AddModulusHandlers()` extension method with explicit DI registrations -- no Scrutor, no reflection, no assembly scanning.

## What It Replaces

Previously, handler registration relied on Scrutor to scan assemblies at runtime:

```csharp
// Old approach (Scrutor)
services.AddModulusMediator(typeof(CatalogModule).Assembly);
// Internally used services.Scan() to find and register handlers
```

The source generator replaces this with compile-time discovery:

```csharp
// New approach (source-generated)
services.AddModulusMediator();     // Registers IMediator only
services.AddModulusHandlers();     // Source-generated handler registrations
```

## Discovered Types

The generator scans your project for classes implementing these interfaces:

| Interface | Category |
|---|---|
| `ICommandHandler<TCommand>` | Command handlers (void return) |
| `ICommandHandler<TCommand, TResult>` | Command handlers (with result) |
| `IQueryHandler<TQuery, TResult>` | Query handlers |
| `IStreamQueryHandler<TQuery, TResult>` | Streaming query handlers |
| `IDomainEventHandler<TEvent>` | Domain event handlers |
| `IIntegrationEventHandler<TEvent>` | Integration event handlers |
| `AbstractValidator<T>` | FluentValidation validators |

All discovered types are registered as `Scoped` services.

## What Gets Generated

The generator produces a `ModulusHandlerRegistrations.g.cs` file containing:

```csharp
public static class ModulusHandlerRegistrations
{
    public static IServiceCollection AddModulusHandlers(this IServiceCollection services)
    {
        // Commands
        services.AddScoped<ICommandHandler<CreateProduct, Guid>, CreateProductHandler>();
        services.AddScoped<ICommandHandler<PlaceOrder>, PlaceOrderHandler>();

        // Queries
        services.AddScoped<IQueryHandler<GetProductById, ProductDto>, GetProductByIdHandler>();

        // Validators
        services.AddScoped<AbstractValidator<CreateProduct>, CreateProductValidator>();

        // Domain Events
        services.AddScoped<IDomainEventHandler<ProductCreated>, ProductCreatedHandler>();

        return services;
    }
}
```

The registrations are sorted by category and then alphabetically by fully qualified name for deterministic output.

## Usage

Call `AddModulusHandlers()` in your module's `ConfigureServices` method:

```csharp
public class CatalogModule : IModuleRegistration
{
    public static IServiceCollection ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModulusMediator();
        services.AddModulusHandlers(); // Source-generated
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));

        return services;
    }

    public static IEndpointRouteBuilder ConfigureEndpoints(IEndpointRouteBuilder app)
    {
        // Map endpoints...
        return app;
    }
}
```

## Performance Benefits

| Aspect | Scrutor (runtime) | Source Generator (compile-time) |
|---|---|---|
| Startup time | Assembly scanning on every start | Zero overhead -- registrations are pre-generated |
| AOT compatibility | Incompatible (requires reflection) | Fully compatible |
| Debugging | Invisible registrations | Generated code is inspectable |
| Error detection | Runtime DI exceptions | Compile-time visibility |

## Generator Diagnostic

| ID | Severity | Message |
|---|---|---|
| MODGEN003 | Info | Open generic handler skipped for registration |

Open generic handlers (handlers with unbound type parameters) cannot be registered as concrete types. The generator skips them and emits an informational diagnostic.

## See Also

- [Module Auto-Discovery](./module-discovery) -- Auto-register modules at the host level
- [Mediator Overview](/mediator/) -- The mediator that dispatches to registered handlers
- [Pipeline Behaviors](/mediator/pipeline-behaviors) -- Behaviors that wrap handler execution
