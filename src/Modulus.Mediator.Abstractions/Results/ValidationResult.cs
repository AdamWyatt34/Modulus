namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents a result that specifically contains validation errors.
/// </summary>
public sealed class ValidationResult : Result
{
    private ValidationResult(Error[] errors)
        : base(false, errors)
    {
    }

    /// <summary>
    /// Creates a <see cref="ValidationResult"/> with the specified validation errors.
    /// </summary>
    public static ValidationResult WithErrors(params Error[] errors) => new(errors);
}
