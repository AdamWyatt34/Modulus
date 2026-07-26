using Modulus.Templates;
using Shouldly;
using Xunit;

namespace Modulus.Templates.Tests;

public class RouteTemplateParserTests
{
    [Fact]
    public void Parse_NoParameters_ReturnsEmpty()
    {
        RouteTemplateParser.Parse("/products").ShouldBeEmpty();
    }

    [Fact]
    public void Parse_BareParameter_DefaultsToString()
    {
        var result = RouteTemplateParser.Parse("/items/{itemId}");

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("itemId");
        result[0].ClrType.ShouldBe("string");
    }

    [Theory]
    [InlineData("int", "int")]
    [InlineData("long", "long")]
    [InlineData("guid", "Guid")]
    [InlineData("bool", "bool")]
    [InlineData("decimal", "decimal")]
    [InlineData("double", "double")]
    [InlineData("float", "float")]
    [InlineData("datetime", "DateTime")]
    [InlineData("alpha", "string")]
    [InlineData("unknownconstraint", "string")]
    public void Parse_Constraint_MapsToClrType(string constraint, string expectedClrType)
    {
        var result = RouteTemplateParser.Parse($"/{{id:{constraint}}}");

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("id");
        result[0].ClrType.ShouldBe(expectedClrType);
    }

    [Fact]
    public void Parse_ConstraintIsCaseInsensitive()
    {
        var result = RouteTemplateParser.Parse("/{id:GUID}");

        result[0].ClrType.ShouldBe("Guid");
    }

    [Fact]
    public void Parse_OptionalMarker_MakesTypeNullable()
    {
        var result = RouteTemplateParser.Parse("/{id:int?}");

        result[0].ClrType.ShouldBe("int?");
    }

    [Fact]
    public void Parse_OptionalWithoutConstraint_MakesStringNullable()
    {
        var result = RouteTemplateParser.Parse("/{name?}");

        result[0].Name.ShouldBe("name");
        result[0].ClrType.ShouldBe("string?");
    }

    [Fact]
    public void Parse_DefaultValueClause_StripsDefaultButKeepsName()
    {
        var result = RouteTemplateParser.Parse("/{page=1}");

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("page");
        result[0].ClrType.ShouldBe("string");
    }

    [Fact]
    public void Parse_MultipleParameters_ReturnsInRouteOrder()
    {
        var result = RouteTemplateParser.Parse("/{parentId:guid}/items/{itemId:guid}");

        result.Count.ShouldBe(2);
        result[0].Name.ShouldBe("parentId");
        result[1].Name.ShouldBe("itemId");
    }

    [Fact]
    public void ToInterpolationTemplate_StripsConstraintButKeepsName()
    {
        RouteTemplateParser.ToInterpolationTemplate("/{id:guid}").ShouldBe("/{id}");
    }

    [Fact]
    public void ToInterpolationTemplate_StripsOptionalMarker()
    {
        RouteTemplateParser.ToInterpolationTemplate("/{id:int?}").ShouldBe("/{id}");
    }

    [Fact]
    public void ToInterpolationTemplate_LeavesBareNameUnchanged()
    {
        RouteTemplateParser.ToInterpolationTemplate("/items/{itemId}").ShouldBe("/items/{itemId}");
    }

    [Fact]
    public void ToInterpolationTemplate_NoParameters_ReturnsUnchanged()
    {
        RouteTemplateParser.ToInterpolationTemplate("/products").ShouldBe("/products");
    }

    [Fact]
    public void ToInterpolationTemplate_MultipleParameters_StripsAllConstraints()
    {
        RouteTemplateParser.ToInterpolationTemplate("/{parentId:guid}/items/{itemId:guid}")
            .ShouldBe("/{parentId}/items/{itemId}");
    }
}
