namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents a typed result that specifically contains validation errors.
/// </summary>
/// <typeparam name="TValue">The type of the value that would have been produced on success.</typeparam>
public sealed class ValidationResult<TValue> : Result<TValue>
{
    private ValidationResult(Error[] errors)
        : base(errors)
    {
    }

    /// <summary>
    /// Creates a <see cref="ValidationResult{TValue}"/> with the specified validation errors.
    /// </summary>
    public static ValidationResult<TValue> WithErrors(params Error[] errors) => new(errors);
}
