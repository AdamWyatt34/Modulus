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

    private Task PublishInternal<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TEvent>>().ToList();

        // MediatorOptions is only registered by AddModulusMediator(configure); a container built
        // by hand (or an older caller that never adopted the configure overload) has none
        // registered, and PublishStrategy.Sequential — the pre-4.0 behavior — is the correct
        // fallback in that case.
        var strategy = serviceProvider.GetService<MediatorOptions>()?.PublishStrategy
            ?? PublishStrategy.Sequential;

        return strategy switch
        {
            PublishStrategy.Parallel => PublishParallel(handlers, domainEvent, cancellationToken),
            PublishStrategy.StopOnFirstFailure => PublishStopOnFirstFailure(handlers, domainEvent, cancellationToken),
            _ => PublishSequential(handlers, domainEvent, cancellationToken),
        };
    }

    private static async Task PublishSequential<TEvent>(
        IReadOnlyList<IDomainEventHandler<TEvent>> handlers,
        TEvent domainEvent,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
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

    private static async Task PublishStopOnFirstFailure<TEvent>(
        IReadOnlyList<IDomainEventHandler<TEvent>> handlers,
        TEvent domainEvent,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in handlers)
        {
            // Same stop-before-dispatch cancellation check as Sequential.
            cancellationToken.ThrowIfCancellationRequested();

            // No try/catch: the first handler failure — expected or a genuine
            // OperationCanceledException raised by the handler itself — propagates immediately,
            // unwrapped, and every handler after it never runs.
            await handler.Handle(domainEvent, cancellationToken);
        }
    }

    private static async Task PublishParallel<TEvent>(
        IReadOnlyList<IDomainEventHandler<TEvent>> handlers,
        TEvent domainEvent,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        // An already-cancelled token still means "stop before dispatching anything", matching
        // Sequential/StopOnFirstFailure's pre-handler check.
        cancellationToken.ThrowIfCancellationRequested();

        // StartHandler shields against a handler that throws synchronously instead of returning a
        // faulted Task: without it, that throw would happen while building `tasks` below — outside
        // the try block — and stop every remaining handler from ever starting.
        var tasks = handlers
            .Select(handler => StartHandler(handler, domainEvent, cancellationToken))
            .ToArray();

        var whenAll = Task.WhenAll(tasks);

        try
        {
            await whenAll;
        }
        catch
        {
            // Unlike Sequential/StopOnFirstFailure, every handler is already started before
            // cancellation can be observed here — an in-flight handler is never interrupted, only
            // observed after every task has settled. Cancellation still takes priority over
            // aggregating handler failures, mirroring Sequential's `catch (OperationCanceledException)
            // { throw; }`.
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            // `await whenAll` only rethrows the first failure, so walk the tasks to report every
            // handler's. Faulted tasks contribute their exceptions; a task that ended Canceled
            // (the handler threw OperationCanceledException from a token of its own — the publish
            // token is checked above and wasn't cancelled) is just as much a handler failure and
            // must be included: Task.WhenAll's own Exception property ignores cancelled tasks
            // entirely, and with no faulted task it is null — aggregating from it alone would
            // throw an AggregateException with zero inner exceptions, losing the failure.
            var failures = new List<Exception>();
            foreach (var task in tasks)
            {
                if (task.Exception is not null)
                {
                    failures.AddRange(task.Exception.InnerExceptions);
                }
                else if (task.IsCanceled)
                {
                    failures.Add(new TaskCanceledException(task));
                }
            }

            if (failures.Count == 0)
            {
                throw; // unreachable in practice: await whenAll only throws when a task failed
            }

            throw new AggregateException(
                $"One or more handlers for {typeof(TEvent).Name} threw an exception.",
                failures);
        }

        // Every handler completed without faulting or being cancelled, but one of them may have
        // cancelled the token as a side effect (the same pattern the CancelingOrderPlacedHandler
        // fixture exercises for Sequential/StopOnFirstFailure) rather than by throwing. Surface
        // that here too, consistent with the other strategies' stop-and-rethrow contract.
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static Task StartHandler<TEvent>(
        IDomainEventHandler<TEvent> handler,
        TEvent domainEvent,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        try
        {
            return handler.Handle(domainEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private async Task<Result> SendCommandInternal<TCommand>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        var handler = serviceProvider.GetService<ICommandHandler<TCommand>>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TCommand).Name}. " +
                $"Ensure a class implementing ICommandHandler<{typeof(TCommand).Name}> is registered.");

        RequestHandlerDelegate<Result> handlerDelegate = ct => handler.Handle(command, ct);

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

        RequestHandlerDelegate<Result<TResult>> handlerDelegate = ct => handler.Handle(command, ct);

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

        RequestHandlerDelegate<Result<TResult>> handlerDelegate = ct => handler.Handle(query, ct);

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

            // Each wrapper closes over `ct` — the token *this* behavior is invoked with — and
            // hands the behavior an inner delegate that substitutes `ct` whenever the behavior
            // calls `next()` with no argument (i.e. the inner call arrives as `default`). This is
            // what lets `await next()` keep meaning "flow my own token" while still letting a
            // behavior that explicitly calls `next(someOtherToken)` (e.g. a timeout's linked
            // token) override the token for every inner behavior and the handler.
            next = ct =>
            {
                RequestHandlerDelegate<TResponse> innerNext = innerCt =>
                    currentNext(innerCt == default ? ct : innerCt);
                return currentBehavior.Handle(request, innerNext, ct);
            };
        }

        return await next(cancellationToken);
    }
}
