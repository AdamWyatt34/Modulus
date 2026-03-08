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

    private static readonly ConcurrentDictionary<Type, MethodInfo> SendCommandCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> SendCommandWithResultCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> QueryCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> StreamCache = new();

    public Task<Result> Send(ICommand command, CancellationToken cancellationToken = default)
    {
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
        var commandType = command.GetType();
        var method = SendCommandWithResultCache.GetOrAdd(commandType,
            t => SendCommandWithResultInternalMethod.MakeGenericMethod(t, typeof(TResult)));

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
        var queryType = query.GetType();
        var method = QueryCache.GetOrAdd(queryType,
            t => QueryInternalMethod.MakeGenericMethod(t, typeof(TResult)));

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
        var queryType = query.GetType();
        var method = StreamCache.GetOrAdd(queryType,
            t => StreamInternalMethod.MakeGenericMethod(t, typeof(TResult)));

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

    public async Task Publish<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TEvent>>();
        var exceptions = new List<Exception>();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.Handle(domainEvent, cancellationToken);
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
