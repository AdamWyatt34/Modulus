using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Modulus.Generators;

/// <summary>
/// A value-equatable stand-in for <see cref="Location"/>. A raw <see cref="Location"/> holds a
/// reference to its <see cref="SyntaxTree"/>, which roots the old tree in incremental generator
/// caches and defeats caching (two logically-identical locations across compilation passes are
/// never <c>Equals</c> because the tree instances differ). This type captures only the file path,
/// span, and line/column data needed to recreate a <see cref="Location"/> on demand.
/// </summary>
internal readonly struct EquatableLocation : IEquatable<EquatableLocation>
{
    public string FilePath { get; }
    public int SpanStart { get; }
    public int SpanLength { get; }
    public LinePosition StartLinePosition { get; }
    public LinePosition EndLinePosition { get; }

    private EquatableLocation(
        string filePath,
        int spanStart,
        int spanLength,
        LinePosition startLinePosition,
        LinePosition endLinePosition)
    {
        FilePath = filePath;
        SpanStart = spanStart;
        SpanLength = spanLength;
        StartLinePosition = startLinePosition;
        EndLinePosition = endLinePosition;
    }

    public static EquatableLocation? FromLocation(Location? location)
    {
        if (location is null || location.SourceTree is null)
            return null;

        var lineSpan = location.GetLineSpan();
        return new EquatableLocation(
            lineSpan.Path ?? string.Empty,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            lineSpan.StartLinePosition,
            lineSpan.EndLinePosition);
    }

    public Location ToLocation()
    {
        if (string.IsNullOrEmpty(FilePath))
            return Location.None;

        return Location.Create(
            FilePath,
            new TextSpan(SpanStart, SpanLength),
            new LinePositionSpan(StartLinePosition, EndLinePosition));
    }

    public bool Equals(EquatableLocation other) =>
        FilePath == other.FilePath &&
        SpanStart == other.SpanStart &&
        SpanLength == other.SpanLength &&
        StartLinePosition.Equals(other.StartLinePosition) &&
        EndLinePosition.Equals(other.EndLinePosition);

    public override bool Equals(object obj) =>
        obj is EquatableLocation other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = FilePath?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ SpanStart;
            hash = (hash * 397) ^ SpanLength;
            hash = (hash * 397) ^ StartLinePosition.GetHashCode();
            hash = (hash * 397) ^ EndLinePosition.GetHashCode();
            return hash;
        }
    }
}
