using FluentValidation;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Internals;

namespace Modulus.Mediator.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validatorList = validators.ToList();
        if (validatorList.Count == 0)
            return await next();

        var validationResults = await Task.WhenAll(
            validatorList.Select(v => v.ValidateAsync(
                new ValidationContext<TRequest>(request),
                cancellationToken)));

        var errors = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))
            .ToArray();

        if (errors.Length > 0)
        {
            return ResultFactory.CreateValidationResult<TResponse>(errors);
        }

        return await next();
    }
}
