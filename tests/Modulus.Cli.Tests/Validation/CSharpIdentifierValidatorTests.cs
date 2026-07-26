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

    // ── H-CLI3: IsValidTypeName accepts the built-in aliases IsValid correctly rejects ────

    [Theory]
    [InlineData("string", true)]
    [InlineData("int", true)]
    [InlineData("bool", true)]
    [InlineData("decimal", true)]
    [InlineData("long", true)]
    [InlineData("double", true)]
    [InlineData("object", true)]
    [InlineData("Guid", true)]
    [InlineData("DateTime", true)]
    [InlineData("DateTimeOffset", true)]
    [InlineData("string?", true)]
    [InlineData("int?", true)]
    [InlineData("Guid?", true)]
    [InlineData("ProductDto", true)]
    [InlineData("Some.Namespaced.Dto", true)]
    [InlineData("123Bad", false)]
    [InlineData("", false)]
    [InlineData("?", false)]
    [InlineData("Foo;Bar", false)]
    public void Validates_type_names(string type, bool expected)
    {
        CSharpIdentifierValidator.IsValidTypeName(type).ShouldBe(expected);
    }

    // ── Generic types, nullable generics, and arrays are now accepted ────────────────────

    [Theory]
    [InlineData("Foo<Bar>", true)]
    [InlineData("List<string>", true)]
    [InlineData("List<Guid>", true)]
    [InlineData("IReadOnlyList<ProductDto>", true)]
    [InlineData("Dictionary<string,int>", true)]
    [InlineData("Dictionary<string, int>", true)]
    [InlineData("Dictionary<string,List<Guid>>", true)]
    [InlineData("List<Dictionary<string,int>>", true)]
    [InlineData("PagedResult<ProductDto>", true)]
    [InlineData("Some.Namespaced.PagedResult<Some.Namespaced.Dto>", true)]
    [InlineData("List<int?>", true)]
    [InlineData("Dictionary<string, List<int>?>", true)]
    public void Validates_generic_type_names(string type, bool expected)
    {
        CSharpIdentifierValidator.IsValidTypeName(type).ShouldBe(expected);
    }

    [Theory]
    [InlineData("int[]", true)]
    [InlineData("string[]", true)]
    [InlineData("Guid[]", true)]
    [InlineData("List<int>[]", true)]
    [InlineData("int[][]", true)]
    [InlineData("int[,]", true)]
    [InlineData("int?[]", true)]
    [InlineData("string[]?", true)]
    public void Validates_array_type_names(string type, bool expected)
    {
        CSharpIdentifierValidator.IsValidTypeName(type).ShouldBe(expected);
    }

    // ── Still-invalid input is rejected with the wider grammar ───────────────────────────

    [Theory]
    [InlineData("Foo<Bar")]
    [InlineData("Foo<Bar>>")]
    [InlineData("Foo<>")]
    [InlineData("Foo<,>")]
    [InlineData("List<>")]
    [InlineData("List<int")]
    [InlineData("Dictionary<string,>")]
    [InlineData("Dictionary<,int>")]
    [InlineData("Foo<Bar>Baz")]
    [InlineData("Foo<class>")]
    [InlineData("int[")]
    [InlineData("int]")]
    [InlineData("int[,")]
    [InlineData("List<int>[")]
    public void Rejects_unbalanced_or_malformed_generic_and_array_syntax(string type)
    {
        CSharpIdentifierValidator.IsValidTypeName(type).ShouldBeFalse();
    }
}
