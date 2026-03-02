using Modulus.Cli.Validation;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Validation;

public class PropertyParserTests
{
    [Fact]
    public void Parse_valid_properties_returns_list()
    {
        var (props, error) = PropertyParser.Parse("Name:string,Email:string,IsActive:bool");

        error.ShouldBeNull();
        props.Count.ShouldBe(3);
        props[0].Name.ShouldBe("Name");
        props[0].Type.ShouldBe("string");
        props[1].Name.ShouldBe("Email");
        props[1].Type.ShouldBe("string");
        props[2].Name.ShouldBe("IsActive");
        props[2].Type.ShouldBe("bool");
    }

    [Fact]
    public void Parse_null_returns_empty_list()
    {
        var (props, error) = PropertyParser.Parse(null);

        error.ShouldBeNull();
        props.Count.ShouldBe(0);
    }

    [Fact]
    public void Parse_empty_returns_empty_list()
    {
        var (props, error) = PropertyParser.Parse("");

        error.ShouldBeNull();
        props.Count.ShouldBe(0);
    }

    [Fact]
    public void Parse_whitespace_returns_empty_list()
    {
        var (props, error) = PropertyParser.Parse("   ");

        error.ShouldBeNull();
        props.Count.ShouldBe(0);
    }

    [Fact]
    public void Parse_missing_colon_returns_error()
    {
        var (_, error) = PropertyParser.Parse("BadFormat");

        error.ShouldNotBeNull();
        error.ShouldContain("BadFormat");
    }

    [Fact]
    public void Parse_missing_type_returns_error()
    {
        var (_, error) = PropertyParser.Parse("Name:");

        error.ShouldNotBeNull();
        error.ShouldContain("Name:");
    }

    [Fact]
    public void Parse_missing_name_returns_error()
    {
        var (_, error) = PropertyParser.Parse(":string");

        error.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_invalid_identifier_returns_error()
    {
        var (_, error) = PropertyParser.Parse("123Bad:string");

        error.ShouldNotBeNull();
        error.ShouldContain("123Bad");
    }

    [Fact]
    public void Parse_trims_whitespace()
    {
        var (props, error) = PropertyParser.Parse(" Name : string , Email : string ");

        error.ShouldBeNull();
        props.Count.ShouldBe(2);
        props[0].Name.ShouldBe("Name");
        props[0].Type.ShouldBe("string");
        props[1].Name.ShouldBe("Email");
        props[1].Type.ShouldBe("string");
    }

    [Fact]
    public void Parse_single_property_works()
    {
        var (props, error) = PropertyParser.Parse("Price:decimal");

        error.ShouldBeNull();
        props.Count.ShouldBe(1);
        props[0].Name.ShouldBe("Price");
        props[0].Type.ShouldBe("decimal");
    }
}
