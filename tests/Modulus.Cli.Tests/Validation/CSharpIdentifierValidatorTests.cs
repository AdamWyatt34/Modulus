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
}
