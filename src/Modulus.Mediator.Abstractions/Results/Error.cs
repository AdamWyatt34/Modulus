namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Represents a discrete error with a code, description, and type classification.
/// </summary>
/// <param name="Code">A machine-readable error code.</param>
/// <param name="Description">A human-readable description of the error.</param>
/// <param name="Type">The category of error.</param>
public readonly record struct Error(string Code, string Description, ErrorType Type = ErrorType.Failure)
{
    /// <summary>
    /// Represents the absence of an error.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Creates a <see cref="ErrorType.Failure"/> error.</summary>
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    /// <summary>Creates a <see cref="ErrorType.Validation"/> error.</summary>
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    /// <summary>Creates a <see cref="ErrorType.NotFound"/> error.</summary>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    /// <summary>Creates a <see cref="ErrorType.Conflict"/> error.</summary>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    /// <summary>Creates a <see cref="ErrorType.Unauthorized"/> error.</summary>
    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    /// <summary>Creates a <see cref="ErrorType.Forbidden"/> error.</summary>
    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);
}
