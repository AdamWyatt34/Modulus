using FluentValidation;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Internals;

namespace Modulus.Mediator.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(
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
