# Mediator Overview

Modulus ships with a custom CQRS mediator built from scratch -- no dependency on MediatR or any other third-party mediator library. It provides commands, queries, domain events, streaming queries, and a configurable pipeline with first-class Result pattern integration.

## Why a Custom Mediator?

| Concern | Modulus Mediator |
|---|---|
| **No external dependency** | The mediator is part of the Modulus ecosystem. No MediatR NuGet reference, no version conflicts, no license concerns. |
| **Tight Result integration** | Every command and query handler returns `Result` or `Result<T>` by design. No casting, no conventions -- the type system enforces it. |
| **Minimal allocations** | The implementation avoids unnecessary object allocations in the hot path, keeping overhead low for high-throughput scenarios. |
| **Streaming support** | `IAsyncEnumerable<T>` streaming queries are a first-class concept, not an afterthought. |
| **Pipeline behaviors** | A composable pipeline with built-in behaviors for validation, logging, exception handling, and metrics. |

## Installation

If you scaffolded your solution with the Modulus CLI, the mediator packages are already referenced. To add them manually:

```bash
# Implementation package (Infrastructure / host projects)
dotnet add package ModulusKit.Mediator

# Abstractions only (Domain / Application layers)
dotnet add package ModulusKit.Mediator.Abstractions
```

::: tip Abstractions package
Reference `ModulusKit.Mediator.Abstractions` in your Domain and Application layers to keep them free of third-party dependencies. The implementation package (`ModulusKit.Mediator`) should only be referenced in Infrastructure and host projects.
:::

## Quick Setup

Register the mediator and pipeline behaviors in your host project's `Program.cs` or composition root:

```csharp
using Modulus.Mediator;

var builder = WebApplication.CreateBuilder(args);

// Register IMediator and auto-discover all handlers via Scrutor
builder.Services.AddModulusMediator(typeof(Program).Assembly);

// Register pipeline behaviors (order matters -- first registered = outermost)
builder.Services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
builder.Services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
```

You can pass multiple assemblies to scan for handlers across all your modules:

```csharp
builder.Services.AddModulusMediator(
    typeof(CatalogModule).Assembly,
    typeof(OrdersModule).Assembly,
    typeof(IdentityModule).Assembly);
```

## IMediator Interface

The `IMediator` interface is the single entry point for dispatching commands, queries, streaming queries, and domain events:

```csharp
public interface IMediator
{
    // Send a command that returns no value
    Task<Result> Send(ICommand command, CancellationToken cancellationToken = default);

    // Send a command that returns a typed value
    Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    // Execute a query
    Task<Result<TResult>> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    // Execute a streaming query
    IAsyncEnumerable<TResult> Stream<TResult>(IStreamQuery<TResult> query, CancellationToken cancellationToken = default);

    // Publish a domain event to all registered handlers
    Task Publish<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}
```

| Method | Input | Return | Description |
|---|---|---|---|
| `Send` | `ICommand` | `Task<Result>` | Dispatches a command with no return value |
| `Send<T>` | `ICommand<T>` | `Task<Result<T>>` | Dispatches a command that produces a value |
| `Query<T>` | `IQuery<T>` | `Task<Result<T>>` | Dispatches a read-only query |
| `Stream<T>` | `IStreamQuery<T>` | `IAsyncEnumerable<T>` | Dispatches a streaming query |
| `Publish` | `IDomainEvent` | `Task` | Publishes a domain event to all handlers |

## Usage at a Glance

```csharp
public class CreateProductEndpoint
{
    public static async Task<IResult> Handle(
        CreateProductCommand command,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);

        return result.Match(
            onSuccess: id => Results.Created($"/products/{id}", id),
            onFailure: errors => Results.BadRequest(errors));
    }
}
```

## What's Next

Dive into the specific concepts:

- **[Commands & Queries](./commands-queries)** -- Define commands, queries, and their handlers
- **[Result Pattern](./result-pattern)** -- Work with `Result<T>`, `Error`, and railway-oriented programming
- **[Pipeline Behaviors](./pipeline-behaviors)** -- Validation, logging, exception handling, and custom behaviors
- **[Domain Events](./domain-events)** -- Publish and handle in-process domain events
- **[Streaming Queries](./streaming)** -- Stream large result sets with `IAsyncEnumerable<T>`
