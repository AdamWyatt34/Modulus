using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Behaviors;

/// <summary>
/// Pipeline behavior that commits an <see cref="IUnitOfWork"/> after a successful command.
/// </summary>
/// <remarks>
/// Resolves <see cref="IUnitOfWork"/> via <see cref="IServiceProvider.GetService(Type)"/>, so
/// consumers that do not register one get a no-op. Queries and failed results bypass the commit.
/// </remarks>
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IServiceProvider serviceProvider)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);

        if (!IsCommand(request) || !response.IsSuccess)
            return response;

        var uow = serviceProvider.GetService<IUnitOfWork>();
        if (uow is not null)
            await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }

    private static bool IsCommand(TRequest request)
    {
        if (request is ICommand)
            return true;

        foreach (var iface in request.GetType().GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ICommand<>))
                return true;
        }

        return false;
    }
}
