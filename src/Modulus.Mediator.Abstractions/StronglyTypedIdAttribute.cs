namespace Modulus.Mediator.Abstractions;

/// <summary>
/// Marks a <c>readonly partial record struct</c> as a strongly typed ID.
/// A source generator will produce the backing value, constructors, and converters
/// (EF Core <c>ValueConverter</c>, System.Text.Json <c>JsonConverter</c>, and <c>TypeConverter</c>).
/// </summary>
/// <param name="backingType">
/// The primitive type used to store the ID value. Supported types: <see cref="Guid"/> (default),
/// <see cref="int"/>, and <see cref="long"/>.
/// </param>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class StronglyTypedIdAttribute(Type? backingType = null) : Attribute
{
    public Type BackingType { get; } = backingType ?? typeof(Guid);
}
