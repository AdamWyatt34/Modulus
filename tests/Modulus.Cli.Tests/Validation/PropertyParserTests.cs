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

    // ── Built-in type aliases are shared with CSharpIdentifierValidator ──────────────────

    [Fact]
    public void Parse_builtin_type_alias_string_is_valid()
    {
        var (props, error) = PropertyParser.Parse("Name:string");

        error.ShouldBeNull();
        props[0].Type.ShouldBe("string");
    }

    // ── Reserved 'Id' name — collides with the base class's own Id property/parameter ────

    [Theory]
    [InlineData("Id")]
    [InlineData("id")]
    [InlineData("ID")]
    public void Parse_rejects_reserved_id_property_name(string name)
    {
        var (props, error) = PropertyParser.Parse($"{name}:string");

        error.ShouldNotBeNull();
        error.ShouldContain("reserved");
        props.ShouldBeEmpty();
    }

    // ── Duplicate property names — generate an uncompilable entity (duplicate member) ────

    [Fact]
    public void Parse_rejects_duplicate_property_names()
    {
        var (props, error) = PropertyParser.Parse("Name:string,Name:int");

        error.ShouldNotBeNull();
        error.ShouldContain("more than once");
        props.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_rejects_duplicate_property_names_differing_only_by_case()
    {
        // EntityGenerator lowercases only the first letter for the factory parameter name, so
        // "Name" and "name" both become the "name" parameter — a guaranteed duplicate-parameter
        // compile error (CS0100) if both were allowed through.
        var (props, error) = PropertyParser.Parse("Name:string,name:int");

        error.ShouldNotBeNull();
        error.ShouldContain("more than once");
    }

    // ── Generic property types — commas nested inside <...> must not split properties ────

    [Fact]
    public void Parse_accepts_generic_list_property_type()
    {
        var (props, error) = PropertyParser.Parse("Tags:List<string>");

        error.ShouldBeNull();
        props.Count.ShouldBe(1);
        props[0].Name.ShouldBe("Tags");
        props[0].Type.ShouldBe("List<string>");
    }

    [Fact]
    public void Parse_dictionary_property_type_is_not_split_on_its_internal_comma()
    {
        var (props, error) = PropertyParser.Parse("Prices:Dictionary<string,decimal>");

        error.ShouldBeNull();
        props.Count.ShouldBe(1);
        props[0].Name.ShouldBe("Prices");
        props[0].Type.ShouldBe("Dictionary<string,decimal>");
    }

    [Fact]
    public void Parse_multiple_properties_including_a_generic_one()
    {
        var (props, error) = PropertyParser.Parse("Name:string,Tags:List<string>,IsActive:bool");

        error.ShouldBeNull();
        props.Count.ShouldBe(3);
        props[0].Type.ShouldBe("string");
        props[1].Name.ShouldBe("Tags");
        props[1].Type.ShouldBe("List<string>");
        props[2].Type.ShouldBe("bool");
    }

    [Fact]
    public void Parse_nested_generic_property_type_with_multiple_commas()
    {
        var (props, error) = PropertyParser.Parse(
            "Lookup:Dictionary<string,List<int>>,Name:string");

        error.ShouldBeNull();
        props.Count.ShouldBe(2);
        props[0].Name.ShouldBe("Lookup");
        props[0].Type.ShouldBe("Dictionary<string,List<int>>");
        props[1].Name.ShouldBe("Name");
    }

    [Fact]
    public void Parse_nullable_and_array_property_types()
    {
        var (props, error) = PropertyParser.Parse("Nickname:string?,Scores:int[]");

        error.ShouldBeNull();
        props.Count.ShouldBe(2);
        props[0].Type.ShouldBe("string?");
        props[1].Type.ShouldBe("int[]");
    }

    [Fact]
    public void Parse_rejects_unbalanced_generic_property_type()
    {
        var (props, error) = PropertyParser.Parse("Tags:List<string");

        error.ShouldNotBeNull();
        error.ShouldContain("List<string");
        props.ShouldBeEmpty();
    }
}
