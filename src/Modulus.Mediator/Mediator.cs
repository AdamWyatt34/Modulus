using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator;

internal sealed class Mediator(IServiceProvider serviceProvider) : IMediator
{
    private static readonly MethodInfo SendCommandInternalMethod =
        typeof(Mediator).GetMethod(nameof(SendCommandInternal), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SendCommandWithResultInternalMethod =
        typeof(Mediator).GetMethod(nameof(SendCommandWithResultInternal), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo QueryInternalMethod =
        typeof(Mediator).GetMethod(nameof(QueryInternal), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo StreamInternalMethod =
        typeof(Mediator).GetMethod(nameof(StreamInternal), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo PublishInternalMethod =
        typeof(Mediator).GetMethod(nameof(PublishInternal), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly ConcurrentDictionary<Type, MethodInfo> SendCommandCache = new();

    // Keyed on (request type, TResult) — a type implementing two closed generic interfaces
    // with different result types (e.g. IQuery<int> and IQuery<string>) must not share a cache entry.
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResultType), MethodInfo> SendCommandWithResultCache = new();
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResultType), MethodInfo> QueryCache = new();
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResultType), MethodInfo> StreamCache = new();

    private static readonly ConcurrentDictionary<Type, MethodInfo> PublishCache = new();

    public Task<Result> Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = command.GetType();
        var method = SendCommandCache.GetOrAdd(commandType,
            t => SendCommandInternalMethod.MakeGenericMethod(t));

        try
        {
            return (Task<Result>)method.Invoke(this, [command, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // unreachable
        }
    }

    public Task<Result<TResult>> Send<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = command.GetType();
        var method = SendCommandWithResultCache.GetOrAdd((commandType, typeof(TResult)),
            key => SendCommandWithResultInternalMethod.MakeGenericMethod(key.RequestType, key.ResultType));

        try
        {
            return (Task<Result<TResult>>)method.Invoke(this, [command, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    public Task<Result<TResult>> Query<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryType = query.GetType();
        var method = QueryCache.GetOrAdd((queryType, typeof(TResult)),
            key => QueryInternalMethod.MakeGenericMethod(key.RequestType, key.ResultType));

        try
        {
            return (Task<Result<TResult>>)method.Invoke(this, [query, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // Pipeline behaviors are not applied to streaming queries.
    // The IAsyncEnumerable<TResult> return type is fundamentally incompatible with
    // the Task<TResponse>-based pipeline behavior model.
    public IAsyncEnumerable<TResult> Stream<TResult>(
        IStreamQuery<TResult> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryType = query.GetType();
        var method = StreamCache.GetOrAdd((queryType, typeof(TResult)),
            key => StreamInternalMethod.MakeGenericMethod(key.RequestType, key.ResultType));

        try
        {
            return (IAsyncEnumerable<TResult>)method.Invoke(this, [query, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // Dispatches on the event's runtime type — the same GetType()/MakeGenericMethod pattern as
    // Send/Query/Stream — so that publishing through a base-typed (e.g. IDomainEvent) variable
    // still resolves the closed IDomainEventHandler<TConcreteEvent> registrations.
    public Task Publish<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        var method = PublishCache.GetOrAdd(eventType,
            t => PublishInternalMethod.MakeGenericMethod(t));

        try
        {
            return (Task)method.Invoke(this, [domainEvent, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private async Task PublishInternal<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TEvent>>();
        var exceptions = new List<Exception>();

        foreach (var handler in handlers)
        {
            // Stop dispatching further handlers once cancellation has been requested instead of
            // burying it in the aggregate below.
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await handler.Handle(domainEvent, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException(
                $"One or more handlers for {typeof(TEvent).Name} threw an exception.",
                exceptions);
        }
    }

    private async Task<Result> SendCommandInternal<TCommand>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        var handler = serviceProvider.GetService<ICommandHandler<TCommand>>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TCommand).Name}. " +
                $"Ensure a class implementing ICommandHandler<{typeof(TCommand).Name}> is registered.");

        RequestHandlerDelegate<Result> handlerDelegate = () => handler.Handle(command, cancellationToken);

        return await ExecutePipeline(command, handlerDelegate, cancellationToken);
    }

    private async Task<Result<TResult>> SendCommandWithResultInternal<TCommand, TResult>(
        TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        var handler = serviceProvider.GetService<ICommandHandler<TCommand, TResult>>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TCommand).Name}. " +
                $"Ensure a class implementing ICommandHandler<{typeof(TCommand).Name}, {typeof(TResult).Name}> is registered.");

        RequestHandlerDelegate<Result<TResult>> handlerDelegate = () => handler.Handle(command, cancellationToken);

        return await ExecutePipeline(command, handlerDelegate, cancellationToken);
    }

    private async Task<Result<TResult>> QueryInternal<TQuery, TResult>(
        TQuery query, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        var handler = serviceProvider.GetService<IQueryHandler<TQuery, TResult>>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TQuery).Name}. " +
                $"Ensure a class implementing IQueryHandler<{typeof(TQuery).Name}, {typeof(TResult).Name}> is registered.");

        RequestHandlerDelegate<Result<TResult>> handlerDelegate = () => handler.Handle(query, cancellationToken);

        return await ExecutePipeline(query, handlerDelegate, cancellationToken);
    }

    private IAsyncEnumerable<TResult> StreamInternal<TQuery, TResult>(
        TQuery query, CancellationToken cancellationToken)
        where TQuery : IStreamQuery<TResult>
    {
        var handler = serviceProvider.GetService<IStreamQueryHandler<TQuery, TResult>>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TQuery).Name}. " +
                $"Ensure a class implementing IStreamQueryHandler<{typeof(TQuery).Name}, {typeof(TResult).Name}> is registered.");

        return handler.Handle(query, cancellationToken);
    }

    private async Task<TResponse> ExecutePipeline<TRequest, TResponse>(
        TRequest request,
        RequestHandlerDelegate<TResponse> handlerDelegate,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        var behaviors = serviceProvider
            .GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .ToList();
        behaviors.Reverse();

        var next = handlerDelegate;
        foreach (var behavior in behaviors)
        {
            var currentNext = next;
            var currentBehavior = behavior;
            next = () => currentBehavior.Handle(request, currentNext, cancellationToken);
        }

        return await next();
    }
}
