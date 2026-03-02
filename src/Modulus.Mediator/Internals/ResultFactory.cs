using Modulus.Mediator.Abstractions;

namespace Modulus.Mediator.Internals;

internal static class ResultFactory
{
    internal static TResponse CreateValidationResult<TResponse>(Error[] errors)
        where TResponse : Result
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(Result)ValidationResult.WithErrors(errors);
        }

        var resultType = typeof(TResponse);
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = resultType.GetGenericArguments()[0];
            var validationResultType = typeof(ValidationResult<>).MakeGenericType(valueType);
            var withErrorsMethod = validationResultType.GetMethod(nameof(ValidationResult.WithErrors))!;
            return (TResponse)withErrorsMethod.Invoke(null, [errors])!;
        }

        throw new InvalidOperationException($"Unexpected response type: {typeof(TResponse)}");
    }

    internal static TResponse CreateFailureResult<TResponse>(params Error[] errors)
        where TResponse : Result
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)Result.Failure(errors);
        }

        var resultType = typeof(TResponse);
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = resultType.GetGenericArguments()[0];
            var failureMethod = typeof(Result<>).MakeGenericType(valueType)
                .GetMethod(nameof(Result.Failure), [typeof(Error[])])!;
            return (TResponse)failureMethod.Invoke(null, [errors])!;
        }

        throw new InvalidOperationException($"Unexpected response type: {typeof(TResponse)}");
    }
}
