using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

public class AiJsonSchemaTests
{
    // CreateStrict: every object closed and fully required, optionality as a ["type","null"] union, and a
    // nullable enum rewritten to anyOf (Anthropic rejects ["string","null"] + enum) without losing its siblings.

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static JsonNode StrictSchema() =>
        JsonNode.Parse(AiJsonSchema.CreateStrict(typeof(SchemaShapes), WebJson).GetRawText())!;

    private static void AssertProperty(string name, string expectedJson)
    {
        var actual = StrictSchema()["properties"]![name];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual),
            $"Schema for '{name}' was:\n{actual?.ToJsonString()}");
    }

    [Fact]
    public void CreateStrict_ClosesAndRequiresEveryProperty()
    {
        var schema = StrictSchema();

        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["nullableEnum", "describedNullableEnum", "requiredEnum", "nullableText"],
            schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void CreateStrict_WritesANullableEnumAsAnyOf() =>
        AssertProperty("nullableEnum", """
            { "anyOf": [{ "type": "string", "enum": ["out", "in"] }, { "type": "null" }] }
            """);

    [Fact]
    public void CreateStrict_KeepsTheDescriptionOfARewrittenNullableEnum() =>
        AssertProperty("describedNullableEnum", """
            {
              "description": "Which way the money moved.",
              "anyOf": [{ "type": "string", "enum": ["out", "in"] }, { "type": "null" }]
            }
            """);

    [Fact]
    public void CreateStrict_LeavesANonNullableEnumAlone() =>
        AssertProperty("requiredEnum", """{ "type": "string", "enum": ["Bank", "Unknown"] }""");

    [Fact]
    public void CreateStrict_LeavesANullableNonEnumAlone() =>
        AssertProperty("nullableText", """{ "type": ["string", "null"] }""");

    private sealed class SchemaShapes
    {
        [AllowedValues("out", "in")]
        public string? NullableEnum { get; set; }

        [AllowedValues("out", "in")]
        [Description("Which way the money moved.")]
        public string? DescribedNullableEnum { get; set; }

        [AllowedValues("Bank", "Unknown")]
        public string RequiredEnum { get; set; } = "";

        public string? NullableText { get; set; }
    }
}
