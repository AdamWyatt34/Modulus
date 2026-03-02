namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Categorizes the type of error that occurred.
/// </summary>
public enum ErrorType
{
    /// <summary>General failure.</summary>
    Failure = 0,

    /// <summary>Validation error.</summary>
    Validation,

    /// <summary>Requested resource was not found.</summary>
    NotFound,

    /// <summary>Conflict with the current state of the resource.</summary>
    Conflict,

    /// <summary>Authentication is required.</summary>
    Unauthorized,

    /// <summary>The caller does not have permission.</summary>
    Forbidden
}
