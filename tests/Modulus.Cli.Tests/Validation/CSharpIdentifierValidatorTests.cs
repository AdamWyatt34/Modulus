using Modulus.Cli.Validation;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Validation;

public class CSharpIdentifierValidatorTests
{
    [Theory]
    [InlineData("Catalog", true)]
    [InlineData("OrderProcessing", true)]
    [InlineData("_internal", true)]
    [InlineData("Module1", true)]
    [InlineData("A", true)]
    [InlineData("123Bad", false)]
    [InlineData("", false)]
    [InlineData("my-module", false)]
    [InlineData("class", false)]
    [InlineData("namespace", false)]
    [InlineData("int", false)]
    [InlineData("string", false)]
    [InlineData("my module", false)]
    [InlineData("hello.world", false)]
    [InlineData("foo!", false)]
    public void Validates_csharp_identifiers(string name, bool expected)
    {
        CSharpIdentifierValidator.IsValid(name).ShouldBe(expected);
    }

    // These tests document the implicit security contract: the validator's rejection of these
    // characters is what makes raw String.Replace token substitution safe across XML (.csproj),
    // JSON (appsettings), and C# contexts. A future contributor who loosens the rule must also
    // ensure those output formats get context-aware encoding.
    [Theory]
    [InlineData("Foo<bar", "XML/HTML tag injection in .csproj or generated markup")]
    [InlineData("Foo>bar", "XML/HTML tag injection in .csproj or generated markup")]
    [InlineData("Foo&bar", "XML entity injection in .csproj")]
    [InlineData("Foo\"bar", "string literal / JSON value escape in templates")]
    [InlineData("Foo'bar", "string literal escape in single-quoted C# / JSON / XML attributes")]
    [InlineData("Foo;bar", "C# statement terminator; could append code in generated handlers")]
    [InlineData("Foo{bar", "JSON object brace / C# block / interpolated string break")]
    [InlineData("Foo}bar", "JSON object brace / C# block / interpolated string break")]
    [InlineData("Foo:bar", "JSON key/value separator injection")]
    [InlineData("Foo(bar", "C# expression injection / method call open")]
    [InlineData("Foo)bar", "C# expression injection / method call close")]
    [InlineData("Foo.bar", "C# member access / namespace traversal; would let a name span namespaces")]
    [InlineData("Foo bar", "whitespace would break identifier-as-token assumption")]
    [InlineData("Foo\\bar", "path separator / C# escape sequence")]
    [InlineData("Foo/bar", "path separator")]
    [InlineData("Foo`bar", "shell metacharacter (POSIX command substitution)")]
    [InlineData("Foo|bar", "shell metacharacter (POSIX pipe)")]
    public void Rejects_security_critical_characters(string name, string reason)
    {
        CSharpIdentifierValidator.IsValid(name)
            .ShouldBeFalse($"validator must reject '{name}' — {reason}");
    }
}
