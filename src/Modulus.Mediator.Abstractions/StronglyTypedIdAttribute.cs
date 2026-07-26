namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Marks a <c>readonly partial record struct</c> as a strongly typed ID.
/// A source generator will produce the backing value, constructors, <c>IComparable&lt;T&gt;</c>,
/// <c>IParsable&lt;T&gt;</c>/<c>TryParse</c> (for minimal API route/query binding), and converters
/// (EF Core <c>ValueConverter</c>, System.Text.Json <c>JsonConverter</c> — including
/// dictionary-key support — and <c>TypeConverter</c>).
/// </summary>
/// <param name="backingType">
/// The primitive type used to store the ID value. Supported types: <see cref="Guid"/> (default),
/// <see cref="int"/>, <see cref="long"/>, and <see cref="string"/>. <see cref="string"/>-backed IDs
/// do not get a <c>New()</c> factory (there is no natural way to generate one) and their
/// constructor rejects a <see langword="null"/> value.
/// </param>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class StronglyTypedIdAttribute(Type? backingType = null) : Attribute
{
    public Type BackingType { get; } = backingType ?? typeof(Guid);
}
