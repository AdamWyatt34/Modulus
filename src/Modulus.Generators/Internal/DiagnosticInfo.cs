using System;
using Microsoft.CodeAnalysis;

namespace Modulus.Generators;

/// <summary>
/// A value-equatable stand-in for <see cref="Diagnostic"/>, used inside incremental generator
/// pipeline models. A raw <see cref="Diagnostic"/> carries a <see cref="Location"/> (which roots
/// a <see cref="SyntaxTree"/>), so comparing pipeline values that capture one directly defeats
/// incremental caching. Only <see cref="EquatableLocation"/> is retained; a real
/// <see cref="Diagnostic"/> is materialized via <see cref="ToDiagnostic"/> in the output stage.
/// </summary>
internal readonly struct DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    public string Id { get; }
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public EquatableLocation? Location { get; }

    public DiagnosticInfo(string id, DiagnosticSeverity severity, string message, EquatableLocation? location)
    {
        Id = id;
        Severity = severity;
        Message = message;
        Location = location;
    }

    public static DiagnosticInfo FromDiagnostic(Diagnostic diagnostic) =>
        new(diagnostic.Id, diagnostic.Severity, diagnostic.GetMessage(), EquatableLocation.FromLocation(diagnostic.Location));

    public Diagnostic ToDiagnostic()
    {
        var descriptor = new DiagnosticDescriptor(
            id: Id,
            title: Id,
            messageFormat: Message,
            category: "ModulusGenerator",
            defaultSeverity: Severity,
            isEnabledByDefault: true);

        return Diagnostic.Create(descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None);
    }

    public bool Equals(DiagnosticInfo other) =>
        Id == other.Id &&
        Severity == other.Severity &&
        Message == other.Message &&
        Equals(Location, other.Location);

    public override bool Equals(object obj) =>
        obj is DiagnosticInfo other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Id?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (int)Severity;
            hash = (hash * 397) ^ (Message?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Location?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
